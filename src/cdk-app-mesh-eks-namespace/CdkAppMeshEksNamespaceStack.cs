using System;
using System.Diagnostics;
using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.EKS;
using CdkShared;

namespace CdkAppMeshEksNamespace
{
    public class CdkAppMeshEksNamespaceStack : BetterStack
    {
        public string EksClusterName => GetClusterName(this);
        public string ExistingVpcId => this.GetCtxString("ExistingVpcId");
        public string KubectlRole => this.GetCtxString("KubectlRole");
        public string EksOidcProviderId => this.GetCtxString("EksOidcProviderId");
        public string MeshedNamespace => GetMeshedNamespace(this);
        public bool AddMeshedNsToFargate => this.GetCtxValue("AddMeshedNsToFargate", false);
        public bool SkipCreatingNamespace => this.GetCtxValue("SkipCreatingNamespace", false);
        public bool AddIngressGatewayToNs => this.GetCtxValue("AddIngressGatewayToNs", true);
        public bool SkipLbControllerInstallation => this.GetCtxValue("SkipLbControllerInstallation", true);
        public bool SkipAppMeshControllerInstallation => this.GetCtxValue("SkipAppMeshControllerInstallation", false);
        public string LbControllerNamespace => this.GetCtxString("LbControllerNamespace", "kube-system");
        public string AppMeshControllerNamespace => this.GetCtxString("AppMeshControllerNamespace", "appmesh-system");
        public bool TraceWithXRayOnAppMesh => this.GetCtxValue<bool>("TraceWithXRayOnAppMesh", true);

        private bool DoNothing => string.IsNullOrEmpty(this.MeshedNamespace)
                                    && !this.AddIngressGatewayToNs
                                    && this.SkipAppMeshControllerInstallation;

        
        private static string GetClusterName(Construct scope)
        {
            const string clusterNameParam = "EksClusterName";
            string clusterName = scope.GetCtxString(clusterNameParam);

            if(string.IsNullOrWhiteSpace(clusterName))
                throw new Exception("EKS cluster name must be specified");

            return clusterName;
        }

        private static string GetMeshedNamespace(Construct scope)
            => scope.GetCtxString("MeshedNamespace", "default");

        private static BetterStackProps InitStackProps(BetterStackProps props)
        {
            props ??= new BetterStackProps();

            props.DynamicStackNameGenerator ??= scope => $"AppMeshNamespace--{GetClusterName(scope)}--{GetMeshedNamespace(scope)}";

            return props;
        }

        internal CdkAppMeshEksNamespaceStack(Construct scope, string id = "CdkAppMeshEksNamespaceStack", BetterStackProps props = null) 
            : base(scope, id, InitStackProps(props))
        {
            if (this.DoNothing)
            {
                Trace.TraceWarning("Did nothing: no namespace to mesh and no K8s controllers to install");
                return;
            }

            var eksCluster = Cluster.FromClusterAttributes(this, "EksCluster", new ClusterAttributes
            {
                ClusterName = this.EksClusterName,
                Vpc = Vpc.FromLookup(this, "EksClusterVPC", new VpcLookupOptions { VpcId = this.ExistingVpcId }),
                KubectlRoleArn = $"arn:aws:iam::{this.Account}:role/{this.KubectlRole}",
                OpenIdConnectProvider = OpenIdConnectProvider.FromOpenIdConnectProviderArn(this, "EksOidcProvider", 
                    $"arn:aws:iam::{this.Account}:oidc-provider/oidc.eks.{this.Region}.amazonaws.com/id/{this.EksOidcProviderId}"
                    )
            });

            if (!this.SkipAppMeshControllerInstallation)
                eksCluster.AddAppMeshController(this.AppMeshControllerNamespace, this.TraceWithXRayOnAppMesh);

            if (this.SkipLbControllerInstallation)
                eksCluster.AddAwsLoadBalancerController(this.LbControllerNamespace);
        }
    }
}
