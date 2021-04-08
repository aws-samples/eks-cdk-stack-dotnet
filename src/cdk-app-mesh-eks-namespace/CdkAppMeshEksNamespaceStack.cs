using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Amazon.CDK;
using Amazon.CDK.AWS.EKS;
using CdkShared;
using OpenIdConnectProvider = Amazon.CDK.AWS.IAM.OpenIdConnectProvider;

namespace CdkAppMeshEksNamespace
{
    public class CdkAppMeshEksNamespaceStack : BetterStack
    {
        public string AppMeshName => this.GetCtxString("AppMeshName", "sample-cdk-app-mesh");
        public bool SkipCreatingAppMesh => this.GetCtxValue<bool>("SkipCreatingAppMesh", false);
        public string EksClusterName => GetClusterName(this);
        public string ExistingVpcId => this.GetCtxString("ExistingVpcId");
        public string KubectlRole => this.GetCtxString("KubectlRole");
        public string EksOidcProviderId => this.GetCtxString("EksOidcProviderId");
        public string MeshedNamespace => GetMeshedNamespace(this);
        public bool MeshedNamespaceIsOnFargate => this.GetCtxValue("MeshedNamespaceIsOnFargate", false);
        public string IngressGatewayNameOverride => this.GetCtxString("IngressGatewayNameOverride").BlankToNull();
        public bool SkipCreatingNamespace => this.GetCtxValue("SkipCreatingNamespace", false);
        public bool AddIngressGatewayToNs => this.GetCtxValue("AddIngressGatewayToNs", true);
        public bool SkipLbControllerInstallation => this.GetCtxValue("SkipLbControllerInstallation", true);
        public bool SkipAppMeshControllerInstallation => this.GetCtxValue("SkipAppMeshControllerInstallation", false);
        public string LbControllerNamespace => this.GetCtxString("LbControllerNamespace", "kube-system");
        public string AppMeshControllerNamespace => this.GetCtxString("AppMeshControllerNamespace", "appmesh-system");
        public bool TraceWithXRayOnAppMesh => this.GetCtxValue<bool>("TraceWithXRayOnAppMesh", true);
        public string EnvoyServiceAccountName => this.GetCtxString("EnvoyServiceAccountName", "envoy-svc-account");
        public string IngressGatewayHelmChartUrl => this.GetCtxString("IngressGatewayHelmChartUrl",
            "https://github.com/aws-samples/aws-app-mesh-helm-chart/raw/main/packaged-charts/eks-app-mesh-gateway-0.1.0.tgz");
        public string IngressGatewayImageTag => this.GetCtxString("IngressGatewayImageTag", "v1.16.1.1-prod");

        public IEnumerable<string> EnvoyServiceAccountManagedPolicies => this.GetCtxStrings(
            "EnvoyServiceAccountManagedPolicies",
            "AWSAppMeshEnvoyAccess,AWSCloudMapDiscoverInstanceAccess,AWSXRayDaemonWriteAccess,CloudWatchLogsFullAccess,AWSCloudMapFullAccess,AWSAppMeshFullAccess");

        private bool NeedToMeshANamespace => !string.IsNullOrEmpty(this.MeshedNamespace);

        private bool DoNothing => !this.NeedToMeshANamespace
                                    && !this.AddIngressGatewayToNs
                                    && this.SkipAppMeshControllerInstallation
                                    && this.SkipCreatingAppMesh
                                    ;

        private string IngressGatewayName => this.IngressGatewayNameOverride ??
                                             $"ingressgw-{this.EksClusterName}-{this.MeshedNamespace}";

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
                Trace.TraceWarning("Did nothing: no namespace to mesh, no App Mesh to create, and no K8s controllers to install");
                return;
            }

            // TODO: add required param check, like AppMeshName, etc.

            var eksCluster = Cluster.FromClusterAttributes(this, "EksCluster", new ClusterAttributes
            {
                ClusterName = this.EksClusterName,
                //Vpc = Vpc.FromLookup(this, "EksClusterVPC", new VpcLookupOptions { VpcId = this.ExistingVpcId }),
                KubectlRoleArn = $"arn:aws:iam::{this.Account}:role/{this.KubectlRole}",
                OpenIdConnectProvider = OpenIdConnectProvider.FromOpenIdConnectProviderArn(this, "EksOidcProvider", 
                   $"arn:aws:iam::{this.Account}:oidc-provider/oidc.eks.{this.Region}.amazonaws.com/id/{this.EksOidcProviderId}"
                   )
            });

            HelmChart lbController = null;
            if (!this.SkipLbControllerInstallation)
                lbController = eksCluster.AddAwsLoadBalancerController(this.LbControllerNamespace);

            HelmChart appMeshController = null;
            if (!this.SkipAppMeshControllerInstallation)
                appMeshController = eksCluster.AddAppMeshController(this.AppMeshControllerNamespace, this.TraceWithXRayOnAppMesh);

            var mesh = eksCluster.AddAppMesh(this.AppMeshName);
            if(appMeshController != null)
                mesh.Node.AddDependency(appMeshController);

            if (!this.NeedToMeshANamespace)
                return;

            IDependable meshedNsDependency = null;
            // 1. Create or not
            if (!this.SkipCreatingNamespace)
            {   // create
                if (Eks.IsStandardNamespace(this.MeshedNamespace))
                    throw new Exception($"Namespace \"{this.MeshedNamespace}\" cannot be created, but the \"{nameof(this.SkipCreatingNamespace)}\" parameter is set to true.");

                var nsLabels = new Dictionary<string, object>
                {
                    ["appmesh.k8s.aws/sidecarInjectorWebhook"] = "enabled",
                    ["mesh"] = this.AppMeshName
                };

                if (this.AddIngressGatewayToNs)
                    nsLabels.Add("gateway", this.IngressGatewayName);

                meshedNsDependency = eksCluster.AddNamespaceIfNecessary(this.MeshedNamespace, nsLabels);
            }
            else
            {   // TODO: kubectl patch existing namespace to add labels
                // kubectl patch with CDK: https://docs.aws.amazon.com/cdk/api/latest/docs/@aws-cdk_aws-eks.KubernetesPatch.html
                // kubectl patch with kubectl: https://kubernetes.io/docs/reference/kubectl/cheatsheet/#patching-resources

            }

            // Create service account for Envoy PODs
            var envoySvcAccount = eksCluster.AddServiceAccount(this.EnvoyServiceAccountName, this.MeshedNamespace, meshedNsDependency, 
                this.EnvoyServiceAccountManagedPolicies.ToArray()
            );
            this.Output("Envoy-Svc-Account", envoySvcAccount.ServiceAccountName, "The name of the K8s service account for App Mesh Envoy proxies");

            // Create Ingress Gateway in the Namespace
            if (this.AddIngressGatewayToNs)
            {
                HelmChart chart = eksCluster.AddHelmChart("aws-ingress-gateway-chart", new HelmChartOptions
                {
                    Chart = this.IngressGatewayHelmChartUrl,
                    Release = this.IngressGatewayName,
                    Namespace = this.MeshedNamespace,
                    Values = new Dictionary<string, object>
                    {
                        ["appMesh"] = new Dictionary<string, object>
                        {
                            ["fargatePodServiceAccount"] = envoySvcAccount.ServiceAccountName
                        },
                        ["image"] = new Dictionary<string, object>
                        {
                            ["tag"] = this.IngressGatewayImageTag,
                            ["awsRegion"] = this.Region
                        },
                        ["ingress"] = new Dictionary<string, object>
                        {
                            ["enabled"] = "true",
                            ["alb"] = "true",
                            ["annotations"] = new Dictionary<string, object>
                            {
                                ["alb.ingress.kubernetes.io/target-type"] = this.MeshedNamespaceIsOnFargate ? "ip" : "instance"
                            }
                        }
                    }
                });
                
                chart.Node.AddDependency(envoySvcAccount);
                if(!this.SkipCreatingAppMesh)
                    chart.Node.AddDependency(mesh);
                if(lbController != null)
                    chart.Node.AddDependency(lbController);
                if(appMeshController != null)
                    chart.Node.AddDependency(appMeshController);
            }
        }
    }
}
