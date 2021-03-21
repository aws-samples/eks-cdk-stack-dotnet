using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.EKS;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.IAM;
using System.Linq;
using System.Collections.Generic;

#nullable enable

namespace EksEtc
{
    public class EksEtcStack : Stack
    {
        internal EksEtcStack(Construct scope, string id, IStackProps? props = null) : base(scope, id, props)
        {
            this.BuildEcrRepos();
            IVpc vpc = this.GetVpc();
            Cluster eksCluster = this.BuildEksCluster(vpc);
            AddAwsLoadBalancerController(eksCluster);
        }

        private IVpc GetVpc()
        {
            string? existingVpcID = this.GetCtxString("OptionalExistingVpcId");
            const string vpcResourceId = "eks-vpc";

            if(existingVpcID != null)
                return Vpc.FromLookup(this, vpcResourceId, new VpcLookupOptions{ VpcId = existingVpcID });
            
            return new Vpc(this, vpcResourceId, new VpcProps { MaxAzs = 3 });
        }

        private Cluster BuildEksCluster(IVpc vpc)
        {
            string k8sVersion = this.GetCtxString("K8sVersion", "1.19");
            string eksClusterName = this.GetCtxString("EksClusterName", "test-cluster-by-cdk");
            double instanceCount = this.GetCtxValue("InstanceCount", 3.0);
            double spotInstanceCount = this.GetCtxValue("SpotInstanceCount", 0.0);
            string[] fargateNamespaces = this.GetCtxStrings("FargateNamespaces")?.ToArray();
            
            bool hasFargate = fargateNamespaces?.Length > 0;
            bool hasEc2LinuxNodes = instanceCount >= 1 || spotInstanceCount >= 1;
            bool hasLinuxCompute = hasEc2LinuxNodes || hasFargate;

            if(!hasLinuxCompute)
                throw new System.Exception($"At least one Linux compute node is required to run KubeDNS.");

            var eksCluster = new Cluster(this, "eks-cluster", new ClusterProps {
                ClusterName = eksClusterName,
                Vpc = vpc,
                // VpcSubnets = new []  { new SubnetSelection { SubnetType = SubnetType.PRIVATE }},
                Version = KubernetesVersion.Of(k8sVersion),
                DefaultCapacity = 0,
                CoreDnsComputeType = hasEc2LinuxNodes ? CoreDnsComputeType.EC2 : CoreDnsComputeType.FARGATE
                //DefaultCapacityInstance = new InstanceType(instanceType)
            });

            if(hasFargate)
            {
                eksCluster.AddFargateProfile("eks-fargate", new FargateProfileOptions {
                    Selectors = fargateNamespaces.Select(ns => new Selector { Namespace = ns }).ToArray()
                });
            }

            if(instanceCount > 0)
            {
                string instanceType = this.GetCtxString("InstanceType", "t3a.small");

                eksCluster.AddNodegroupCapacity("eks-node-group", new NodegroupOptions  {
                    InstanceTypes = new InstanceType[] { new InstanceType(instanceType) },
                    MinSize = instanceCount,
                    //DiskSize = 100,
                    //AmiType = NodegroupAmiType.AL2_X86_64_GPU,
                });
            }

            if(spotInstanceCount > 0)
            {
                eksCluster.AddNodegroupCapacity("eks-spot-node-group", new NodegroupOptions {
                    CapacityType = CapacityType.SPOT,
                    InstanceTypes =  this.GetCtxStrings("SpotInstanceTypes")?
                                        .Select(instanceTypeString => new InstanceType(instanceTypeString))
                                        .ToArray(),
                    MinSize = spotInstanceCount,
                });
            }

            return eksCluster;
        }

        private HelmChart? AddAwsLoadBalancerController(Cluster eksCluster)
        {
            bool addAlbController = this.GetCtxValue<bool>("InstallAwsLbController", false);
            if(!addAlbController)
                return null;
            
            string lbControllerNamespace = this.GetCtxString("LbControllerNamespace", "kube-system");
            
            ServiceAccount svcAccount = CreateLbControllerServiceAccount(eksCluster, lbControllerNamespace);
            
            return eksCluster.AddHelmChart("aws-load-balancer-controller-chart", new HelmChartOptions {
                Repository = "https://aws.github.io/eks-charts",
                Chart = "aws-load-balancer-controller",
                Namespace = lbControllerNamespace,
                Values = new Dictionary<string,object> {
                    ["fullnameOverride"] = "aws-lb-controller-vh",
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

        private static ServiceAccount CreateLbControllerServiceAccount(Cluster eksCluster, string k8sNamespace)
        {
            ServiceAccount svcAccount = eksCluster.AddServiceAccount("aws-lb-controller-svc-account", new ServiceAccountOptions {
                Name = "aws-lb-controller",
                Namespace = k8sNamespace
            });

            IEnumerable<object> lbControllerIamPolicy = GetLbControllerIamPolicyStatements();
            foreach(object iamPolicyJsonItem in lbControllerIamPolicy)
            {
                PolicyStatement iamPolicyStatement = PolicyStatement.FromJson(iamPolicyJsonItem);
                svcAccount.AddToPrincipalPolicy(iamPolicyStatement);
            }
            return svcAccount;
        }

        private static IEnumerable<object> GetLbControllerIamPolicyStatements()
        {
            const string iamPolicyUrl = "https://raw.githubusercontent.com/kubernetes-sigs/aws-load-balancer-controller/main/docs/install/iam_policy.json";
            string iamPolicyJson = Utils.ReadContentFromUrl(iamPolicyUrl);
            var parsedPolicy = (Dictionary<string, object>?)Utils.JsonToMap(iamPolicyJson);
            var policyStatements = (IEnumerable<object>)parsedPolicy["Statement"];
            return policyStatements;
        }

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
                new CfnOutput(this, "Docker-Login-For-ECR", new CfnOutputProps {
                    Description = "AWS CLI command to authorize docker to push container images to AWS Elastic Container Service (ECR)",
                    Value = $"aws ecr get-login-password --region {this.Region} | docker login --username AWS --password-stdin {repo.RepositoryUri.Substring(0, repo.RepositoryUri.LastIndexOf("/"))}"
                });

            return repos;
        }
    }
}
