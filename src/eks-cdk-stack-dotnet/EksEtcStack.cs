using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.EKS;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.IAM;
using System.Linq;
using System.Collections.Generic;
using System;

// ReSharper disable ObjectCreationAsStatement

#nullable enable

namespace EksEtc
{
    public class EksEtcStack : Stack
    {
        internal EksEtcStack(Construct scope, string id, IStackProps? props = null) : base(scope, id, props)
        {
            _ = this.BuildEcrRepos();
            IVpc vpc = this.GetVpc();
            Cluster eksCluster = this.BuildEksCluster(vpc);
            _ = this.AddClusterAdminIamRoles(eksCluster);
            _ = AddAwsLoadBalancerController(eksCluster);
        }

        /// <summary>
        /// Depending on context parameters, either creates new a VPC,
        /// or returns existing VPC specified by the "OptionalExistingVpcId"
        /// CDK context parameter
        /// </summary>
        /// <returns></returns>
        private IVpc GetVpc()
        {
            string? existingVpcId = this.GetCtxString("OptionalExistingVpcId");
            const string vpcResourceId = "eks-vpc";

            if(existingVpcId != null)
                // Use existing VPC (it will need to have private subnets)
                return Vpc.FromLookup(this, vpcResourceId, new VpcLookupOptions{ VpcId = existingVpcId });
            
            // Create a new VPC specifically for the EKS
            return new Vpc(this, vpcResourceId, new VpcProps { MaxAzs = 3 });
        }

        /// <summary>
        /// Creates new EKS cluster, adds nodegroups defined by CDK context parameters
        /// </summary>
        /// <param name="vpc"></param>
        /// <returns></returns>
        private Cluster BuildEksCluster(IVpc vpc)
        {
            // Using CDK Context parameters (cdk.json) rather than CloudFormation parameters.
            // The reason is that CFN parameters, especially non-string ones, are not always supported.
            // Another assumption is that it's not hard to run "cdk deploy" from a CI/CD pipeline.
            string k8sVersion = this.GetCtxString("K8sVersion", "1.19");
            string eksClusterName = this.GetCtxString("EksClusterName", "test-cluster-by-cdk");
            double onDemandInstanceCount = this.GetCtxValue("OnDemandInstanceCount", 3.0);
            double spotInstanceCount = this.GetCtxValue("SpotInstanceCount", 0.0);
            string[]? fargateNamespaces = this.GetCtxStrings("FargateNamespaces")?.ToArray();
            double gravitonInstanceCount = this.GetCtxValue("GravitonArmInstanceCount", 0.0);
            
            bool hasFargate = fargateNamespaces?.Length > 0;
            bool hasEc2LinuxNodes = onDemandInstanceCount >= 1 || spotInstanceCount >= 1;
            bool hasGravitonNodes = gravitonInstanceCount >= 1;
            bool hasLinuxCompute = hasEc2LinuxNodes || hasFargate || hasGravitonNodes;

            if(!hasLinuxCompute)
                throw new Exception($"At least one Linux compute node is required to run KubeDNS.");

            var eksCluster = new Cluster(this, "eks-cluster", new ClusterProps {
                ClusterName = eksClusterName,
                Vpc = vpc,
                Version = KubernetesVersion.Of(k8sVersion),
                DefaultCapacity = 0,
                CoreDnsComputeType = hasEc2LinuxNodes ? CoreDnsComputeType.EC2 : CoreDnsComputeType.FARGATE
            });

            _ = this.AddNodeGroups(eksCluster, hasFargate, fargateNamespaces, 
                        gravitonInstanceCount, onDemandInstanceCount, spotInstanceCount
                );

            return eksCluster;
        }

        /// <summary>
        /// Adds various compute node types to the cluster: regular on-demand EC2 x86 nodes, Fargate nodes, Graviton nodes, Spot instances.
        /// </summary>
        /// <param name="eksCluster"></param>
        /// <param name="hasFargate"></param>
        /// <param name="fargateNamespaces">Fargate nodes will be used only if at least one K8s namespace will be assigned to AWS Fargate</param>
        /// <param name="gravitonInstanceCount"></param>
        /// <param name="onDemandInstanceCount"></param>
        /// <param name="spotInstanceCount"></param>
        /// <returns></returns>
        private List<Nodegroup> AddNodeGroups(Cluster eksCluster, 
                bool hasFargate, string[]? fargateNamespaces, 
                double gravitonInstanceCount,
                double onDemandInstanceCount,
                double spotInstanceCount)
        {
            var nodeGroups = new List<Nodegroup>();

            if (hasFargate)
            {
                eksCluster.AddFargateProfile("eks-fargate", new FargateProfileOptions
                {
                    Selectors = fargateNamespaces?.Select(ns => new Selector { Namespace = ns }).ToArray()
                });
            }

            if (gravitonInstanceCount > 0)
            {
                string instanceType = this.GetCtxString("GravitonArmInstanceType", "t4g.medium");
                Nodegroup? nodeGroup = eksCluster.AddNodeGroup("eks-graviton-arm-node-group", instanceType, gravitonInstanceCount,
                                        CapacityType.ON_DEMAND, NodegroupAmiType.AL2_ARM_64
                                    );
                nodeGroups.Add(nodeGroup);
            }

            if (onDemandInstanceCount > 0)
            {
                string instanceType = this.GetCtxString("OnDemandInstanceType", "t3a.small");
                Nodegroup? nodeGroup = eksCluster.AddNodeGroup("eks-node-group", instanceType, onDemandInstanceCount);
                nodeGroups.Add(nodeGroup);
            }

            if (spotInstanceCount > 0)
            {
                Nodegroup? nodeGroup = eksCluster.AddNodeGroup("eks-spot-node-group", this.GetCtxStrings("SpotInstanceTypes"),
                    spotInstanceCount, CapacityType.SPOT
                );
                nodeGroups.Add(nodeGroup);
            }

            return nodeGroups;
        }

        /// <summary>
        /// Installs AWS Load Balancer Controller to let outside traffic into the cluster via AWS Application Load Balancer.
        /// </summary>
        /// <param name="eksCluster"></param>
        /// <returns></returns>
        private HelmChart? AddAwsLoadBalancerController(Cluster eksCluster)
        {
            bool addAlbController = this.GetCtxValue<bool>("InstallAwsLbController", false);
            if(!addAlbController)
                return null;
            
            string lbControllerNamespace = this.GetCtxString("LbControllerNamespace", "kube-system");
            
            ServiceAccount svcAccount = CreateLbControllerServiceAccount(eksCluster, lbControllerNamespace);
            
            // Runs Helm chart installing AWS LB controller.
            // The manifest will be accessible via "helm list" on the system where "cdk deploy" was run.
            return eksCluster.AddHelmChart("aws-load-balancer-controller-chart", new HelmChartOptions {
                Repository = "https://aws.github.io/eks-charts",
                Chart = "aws-load-balancer-controller",
                Namespace = lbControllerNamespace,
                Values = new Dictionary<string,object> {
                    ["fullnameOverride"] = "aws-lb-controller",
                    ["clusterName"] = eksCluster.ClusterName,
                    ["serviceAccount"] = new Dictionary<string, object> {
                        ["create"] = false,
                        ["name"] = svcAccount.ServiceAccountName,
                    },
                    ["vpcId"] = eksCluster.Vpc.VpcId,
                    ["region"] = Stack.Of(this).Region
                }
            });
        }

        /// <summary>
        /// Creates K8s service account for AWS LB Controller to run under.
        /// Attaches appropriate IAM policies to the service account to let AWS LB Controller do its job.
        /// </summary>
        /// <param name="eksCluster"></param>
        /// <param name="k8sNamespace">K8s namespace where ALB Ingress Controller will be installed</param>
        /// <returns></returns>
        private static ServiceAccount CreateLbControllerServiceAccount(Cluster eksCluster, string k8sNamespace)
        {
            ServiceAccount svcAccount = eksCluster.AddServiceAccount("aws-lb-controller-svc-account", new ServiceAccountOptions {
                Name = "aws-lb-controller",
                Namespace = k8sNamespace
            });

            IEnumerable<object>? lbControllerIamPolicy = DownloadLbControllerIamPolicyStatements();
            foreach(object iamPolicyJsonItem in lbControllerIamPolicy)
            {
                PolicyStatement iamPolicyStatement = PolicyStatement.FromJson(iamPolicyJsonItem);
                svcAccount.AddToPrincipalPolicy(iamPolicyStatement);
            }
            return svcAccount;
        }

        /// <summary>
        /// Downloads IAM Policy JSON file for AWS LB Controller
        /// </summary>
        /// <returns></returns>
        private static IEnumerable<object>? DownloadLbControllerIamPolicyStatements()
        {
            // This IAM policy is AWS-supported (https://github.com/kubernetes-sigs/aws-load-balancer-controller/blob/main/docs/install/iam_policy.json)
            // and is part of AWS-recommended routine of installing AWS LB Controller (https://docs.aws.amazon.com/eks/latest/userguide/aws-load-balancer-controller.html)
            const string iamPolicyUrl = "https://raw.githubusercontent.com/kubernetes-sigs/aws-load-balancer-controller/main/docs/install/iam_policy.json";
            
            string iamPolicyJson = Utils.ReadContentFromUrl(iamPolicyUrl);
            var parsedPolicy = (Dictionary<string, object>?)Utils.JsonToMap(iamPolicyJson);
            var policyStatements = (IEnumerable<object>?)parsedPolicy?["Statement"];
            return policyStatements;
        }

        /// <summary>
        /// Provisions ECR repositories, if repository names were specified.
        /// </summary>
        /// <returns></returns>
        private List<Repository>? BuildEcrRepos()
        {
            List<Repository>? repos = this.GetCtxStrings("EcrRepoNames")?
                .Select(ecrRepoName =>
                    new Repository(this, $"EksEtc-ECR-{ecrRepoName}-repo", new RepositoryProps{
                        RepositoryName = ecrRepoName,
                        RemovalPolicy = RemovalPolicy.DESTROY,
                    })
                )
                .ToList();
            
            Repository? repo = repos?.FirstOrDefault();
            if(repo != null)
                // Create stack output simplifying `docker` CLI authorization to upload container images to the ECR
                new CfnOutput(this, "Docker-Login-For-ECR", new CfnOutputProps {
                    Description = "AWS CLI command to authorize docker to push container images to AWS Elastic Container Service (ECR)",
                    Value = $"aws ecr get-login-password --region {this.Region} | docker login --username AWS --password-stdin {repo.RepositoryUri.Substring(0, repo.RepositoryUri.LastIndexOf("/"))}"
                });

            return repos;
        }

        /// <summary>
        /// Grants given IAM roles administrator rights to the cluster.
        /// Useful when IAM user running "cdk deploy" CLI, is different from the role used for AWS web console.
        /// </summary>
        /// <param name="eksCluster"></param>
        /// <returns></returns>
        private List<IRole>? AddClusterAdminIamRoles(Cluster eksCluster)
        {
            var adminRoleNames = this.GetCtxStrings("ExistingIamRolesToAllowEksManagement")?.ToList();
            if(adminRoleNames?.FirstOrDefault() == null)
                return null;

            var roleEnum = from adminRoleName in adminRoleNames
                           select Role.FromRoleArn(this, $"eks-admin-role-{adminRoleName}", $"arn:aws:iam::{this.Account}:role/{adminRoleName}");
            var adminRoles = roleEnum.ToList();
            
            var eksGroups = new[] { "system:masters" };

            foreach(IRole iamAdminRole in adminRoles)
            {
                eksCluster.AwsAuth.AddRoleMapping(iamAdminRole, new AwsAuthMapping
                {
                    Groups = eksGroups,
                    Username = iamAdminRole.RoleName
                });
            }

            return adminRoles;
        }
    }
}
