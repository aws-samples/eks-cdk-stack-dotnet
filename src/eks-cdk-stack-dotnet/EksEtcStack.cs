using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.EKS;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.IAM;
using System.Linq;
using System.Collections.Generic;
using System;
using CdkShared;

// ReSharper disable ObjectCreationAsStatement

#nullable enable

namespace EksEtc
{
    public class EksEtcStack : BetterStack
    {
        #region Properties mapped to CDK context parameters (cdk.json)

        public string? ExistingVpcId => this.GetCtxString("OptionalExistingVpcId");
        public string K8sVersion => this.GetCtxString("K8sVersion", "1.19");
        public string EksClusterName => GetClusterName(this);
        public double OnDemandInstanceCount => this.GetCtxValue("OnDemandInstanceCount", 3.0);
        public double SpotInstanceCount => this.GetCtxValue("SpotInstanceCount", 0.0);
        public string[] FargateNamespaces => this.GetCtxStrings("FargateNamespaces")?.ToArray() ?? new string[0];
        public double GravitonInstanceCount => this.GetCtxValue("GravitonArmInstanceCount", 0.0);
        public string GravitonInstanceType => this.GetCtxString("GravitonArmInstanceType", "t4g.medium");
        public string OnDemandInstanceType => this.GetCtxString("OnDemandInstanceType", "t3a.small");
        public IEnumerable<string> SpotInstanceTypes => this.GetCtxStrings("SpotInstanceTypes") ?? Enumerable.Empty<string>();
        public bool ShouldAddAlbController => this.GetCtxValue<bool>("InstallAwsLbController", false);
        public string LbControllerNamespace => this.GetCtxString("LbControllerNamespace", "kube-system");
        public IEnumerable<string> EcrRepoNames => this.GetCtxStrings("EcrRepoNames") ?? Enumerable.Empty<string>();
        public IEnumerable<string> AdminRoleNames => this.GetCtxStrings("ExistingIamRolesToAllowEksManagement")?.ToList() ?? Enumerable.Empty<string>();
        
        #endregion Properties mapped to CDK context parameters (cdk.json)

        public bool HasFargate => this.FargateNamespaces?.Length > 0;
        public bool HasEc2LinuxNodes => this.OnDemandInstanceCount >= 1 || this.SpotInstanceCount >= 1;
        public bool HasGravitonNodes => this.GravitonInstanceCount >= 1;
        public bool HasLinuxCompute => this.HasEc2LinuxNodes || this.HasFargate || this.HasGravitonNodes;
        public bool HasOnlyFargate => this.HasFargate && !this.HasEc2LinuxNodes && !this.HasGravitonNodes;

        private static string GetClusterName(Construct scope) 
            => scope.GetCtxString("EksClusterName", "test-cluster-by-cdk");

        private static BetterStackProps? InitStackProps(BetterStackProps? props)
        {
            props ??= new BetterStackProps();

            props.DynamicStackNameGenerator ??= scope => $"EksEtcStack--{GetClusterName(scope)}";

            return props;
        }

        internal EksEtcStack(Construct scope, string id = "EksEtcStack", BetterStackProps? props = null) 
            : base(scope, id, InitStackProps(props))
        {
            Console.WriteLine($"OnDemandInstanceCount = {OnDemandInstanceCount}");
            Console.WriteLine($"SpotInstanceCount = {SpotInstanceCount}");
            Console.WriteLine($"GravitonInstanceCount = {GravitonInstanceCount}");
            Console.WriteLine($"HasOnlyFargate = {HasOnlyFargate}");

            _ = this.BuildEcrRepos();
            IVpc vpc = this.GetVpc();
            Cluster eksCluster = this.BuildEksCluster(vpc);
            _ = this.AddClusterAdminIamRoles(eksCluster);
            _ = this.AddAwsLoadBalancerController(eksCluster);
        }

        /// <summary>
        /// Depending on context parameters, either creates new a VPC,
        /// or returns existing VPC specified by the "OptionalExistingVpcId"
        /// CDK context parameter
        /// </summary>
        /// <returns></returns>
        private IVpc GetVpc()
        {
            const string vpcResourceId = "eks-vpc";

            if(this.ExistingVpcId != null)
                // Use existing VPC (it will need to have private subnets)
                return Vpc.FromLookup(this, vpcResourceId, new VpcLookupOptions{ VpcId = this.ExistingVpcId });
            
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
            if(!HasLinuxCompute)
                throw new Exception($"At least one Linux compute node is required to run KubeDNS.");

            var eksCluster = new Cluster(this, "eks-cluster", new ClusterProps {
                ClusterName = EksClusterName,
                Vpc = vpc,
                Version = KubernetesVersion.Of(K8sVersion),
                DefaultCapacity = 0,
                CoreDnsComputeType = HasEc2LinuxNodes ? CoreDnsComputeType.EC2 : CoreDnsComputeType.FARGATE
            });
            this.Output("KubeCtlRoleARN", eksCluster.KubectlRole.RoleArn, "kubectl role ARN");
            this.Output("EksOidcConnectProviderARN", eksCluster.OpenIdConnectProvider.OpenIdConnectProviderArn, "EKS OIDC Provider ARN");
            this.Output("EksClusterVpcId", eksCluster.Vpc.VpcId, "EKS cluster VPC Id");

            _ = this.AddNodeGroups(eksCluster);

            return eksCluster;
        }

        /// <summary>
        /// Adds various compute node types to the cluster: regular on-demand EC2 x86 nodes, Fargate nodes, Graviton nodes, Spot instances.
        /// </summary>
        /// <param name="eksCluster"></param>
        /// <returns></returns>
        private List<Nodegroup> AddNodeGroups(Cluster eksCluster)
        {
            var nodeGroups = new List<Nodegroup>();

            if (this.HasFargate)
            {
                foreach(string fargateNamespace in this.FargateNamespaces)
                    eksCluster.AddFargateProfile($"fargate-profile-{fargateNamespace}", new FargateProfileOptions
                    {
                        FargateProfileName = $"{fargateNamespace}-ns",
                        Selectors = new ISelector[] { new Selector { Namespace = fargateNamespace }}
                    });
            }

            if (this.HasGravitonNodes)
            {
                Nodegroup? nodeGroup = eksCluster.AddNodeGroup("eks-graviton-arm-node-group", 
                                        GravitonInstanceType, this.GravitonInstanceCount,
                                        CapacityType.ON_DEMAND, NodegroupAmiType.AL2_ARM_64
                                    );
                nodeGroups.Add(nodeGroup);
            }

            if (this.OnDemandInstanceCount >= 1)
            {
                Nodegroup? nodeGroup = eksCluster.AddNodeGroup("eks-node-group", OnDemandInstanceType, this.OnDemandInstanceCount);
                nodeGroups.Add(nodeGroup);
            }

            if (this.SpotInstanceCount >= 1)
            {
                Nodegroup? nodeGroup = eksCluster.AddNodeGroup("eks-spot-node-group", this.SpotInstanceTypes,
                    this.SpotInstanceCount, CapacityType.SPOT
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
            return ShouldAddAlbController ?
                eksCluster.AddAwsLoadBalancerController(this.LbControllerNamespace)
                : null;
        }

        /// <summary>
        /// Provisions ECR repositories, if repository names were specified.
        /// </summary>
        /// <returns></returns>
        private List<Repository>? BuildEcrRepos()
        {
            List<Repository> repos = this.EcrRepoNames.Select(ecrRepoName =>
                    new Repository(this, $"EksEtc-ECR-{ecrRepoName}-repo", new RepositoryProps{
                        RepositoryName = ecrRepoName,
                        RemovalPolicy = RemovalPolicy.DESTROY,
                    })
                )
                .ToList();
            
            Repository? repo = repos.FirstOrDefault();
            if(repo != null)
                // Create stack output simplifying `docker` CLI authorization to upload container images to the ECR
                this.Output("Docker-Login-For-ECR",
                    $"aws ecr get-login-password --region {this.Region} | docker login --username AWS --password-stdin {repo.RepositoryUri.Substring(0, repo.RepositoryUri.LastIndexOf("/"))}",
                    "AWS CLI command to authorize docker to push container images to AWS Elastic Container Service (ECR)"
                );

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
            if(AdminRoleNames.FirstOrDefault() == null)
                return null;

            var roleEnum = from adminRoleName in AdminRoleNames
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

        /// <summary>
        /// Creates K8s namespace if namespace name is not "default", "kube-system", or "kube-public"
        /// </summary>
        /// <param name="eksCluster"></param>
        /// <param name="namespaceName"></param>
        /// <returns></returns>
        private KubernetesManifest? AddNamespaceIfNecessary(Cluster eksCluster, string namespaceName)
        {
            if (this.HasOnlyFargate && !this.FargateNamespaces.Contains(namespaceName))
                throw new Exception($"Fargate is the only type of nodes specified, but namespace \"{namespaceName}\" is not among Fargate namespaces: \"{string.Join(",", this.FargateNamespaces)}\"");

            return eksCluster.AddNamespaceIfNecessary(namespaceName);
        }
    }
}
