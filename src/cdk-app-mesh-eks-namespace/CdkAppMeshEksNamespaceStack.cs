using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.EKS;
using CdkShared;
using OpenIdConnectProvider = Amazon.CDK.AWS.IAM.OpenIdConnectProvider;

namespace CdkAppMeshEksNamespace
{
    public class CdkAppMeshEksNamespaceStack : BetterStack
    {
        #region Properties mapped to CDK context parameters (cdk.json)

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
        public string IngressGatewayImageTag => this.GetCtxString("IngressGatewayImageTag", "v1.17.2.0-prod");

        public IEnumerable<string> EnvoyServiceAccountManagedPolicies => this.GetCtxStrings(
            "EnvoyServiceAccountManagedPolicies",
            "AWSAppMeshEnvoyAccess,AWSCloudMapDiscoverInstanceAccess,AWSXRayDaemonWriteAccess,CloudWatchLogsFullAccess,AWSCloudMapFullAccess,AWSAppMeshFullAccess");

        #endregion

        private bool NeedToMeshANamespace => !string.IsNullOrEmpty(this.MeshedNamespace);

        private bool DoNothing => !this.NeedToMeshANamespace
                                    && !this.AddIngressGatewayToNs
                                    && this.SkipAppMeshControllerInstallation
                                    && this.SkipCreatingAppMesh
                                    ;

        private string IngressGatewayName => this.IngressGatewayNameOverride ??
                                             $"ingressgw-{this.EksClusterName}-{this.MeshedNamespace}";

        internal CdkAppMeshEksNamespaceStack(Construct scope, string id = "CdkAppMeshEksNamespaceStack", BetterStackProps props = null) 
            : base(scope, id, InitStackProps(props))
        {
            if (this.DoNothing)
            {
                Trace.TraceWarning("Did nothing: no namespace to mesh, no App Mesh to create, and no K8s controllers to install");
                return;
            }

            var argErrors = string.Join('\n', this.ValidateStackArguments());
            if (!argErrors.IsNullOrBlank())
                throw new Exception(argErrors);

            ICluster eksCluster = this.GetEksCluster();
            HelmChart lbController = this.GetLbController(eksCluster);
            HelmChart appMeshController = this.GetAppMeshController(eksCluster);
            KubernetesManifest appMesh = this.AddAppMesh(eksCluster, appMeshController);

            if (this.NeedToMeshANamespace)
                this.AddNamespaceToMesh(eksCluster, appMesh, appMeshController, lbController);
        }

        private void AddNamespaceToMesh(ICluster eksCluster, KubernetesManifest appMesh, HelmChart appMeshController, HelmChart lbController)
        {
            IDependable meshedNsDependency = this.SkipCreatingNamespace
                ? this.LabelExistingNamespaceForMesh(eksCluster)
                : this.CreateNewNamespaceAndAddItToMesh(eksCluster);
            
            ServiceAccount envoySvcAccount = this.CreateEnvoyServiceAcc(eksCluster, meshedNsDependency);

            // Create Ingress Gateway in the Namespace
            if (this.AddIngressGatewayToNs)
                _ = this.AddIngressGatewayToMeshedNamespace(eksCluster, appMesh, appMeshController, lbController, envoySvcAccount);
        }

        private HelmChart AddIngressGatewayToMeshedNamespace(ICluster eksCluster, KubernetesManifest appMesh,
            HelmChart appMeshController, HelmChart lbController, ServiceAccount envoySvcAccount)
        {
            HelmChart igwChart = eksCluster.AddHelmChart("aws-ingress-gateway-chart", new HelmChartOptions
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

            igwChart.Node.AddDependency(envoySvcAccount);
            if (!this.SkipCreatingAppMesh)
                igwChart.Node.AddDependency(appMesh);
            if (lbController != null)
                igwChart.Node.AddDependency(lbController);
            if (appMeshController != null)
                igwChart.Node.AddDependency(appMeshController);
            return igwChart;
        }

        private ServiceAccount CreateEnvoyServiceAcc(ICluster eksCluster, IDependable meshedNsDependency)
        {
            var envoySvcAccount = eksCluster.AddServiceAccount(this.EnvoyServiceAccountName, this.MeshedNamespace,
                meshedNsDependency,
                this.EnvoyServiceAccountManagedPolicies.ToArray()
            );
            this.Output("Envoy-Svc-Account", envoySvcAccount.ServiceAccountName,
                "The name of the K8s service account for App Mesh Envoy proxies");
            return envoySvcAccount;
        }

        private IDependable LabelExistingNamespaceForMesh(ICluster eksCluster)
        {
            // TODO: kubectl patch existing namespace to add labels
            // kubectl patch with CDK: https://docs.aws.amazon.com/cdk/api/latest/docs/@aws-cdk_aws-eks.KubernetesPatch.html
            // kubectl patch with kubectl: https://kubernetes.io/docs/reference/kubectl/cheatsheet/#patching-resources
            throw new NotImplementedException(nameof(LabelExistingNamespaceForMesh));
        }

        private IDependable CreateNewNamespaceAndAddItToMesh(ICluster eksCluster)
        {
            IDependable meshedNsDependency;
            // create
            if (Eks.IsStandardNamespace(this.MeshedNamespace))
            {
                throw new Exception(
                    $"Namespace \"{this.MeshedNamespace}\" cannot be created as it's a standard K8s namespace, " +
                    $"but the \"{nameof(this.SkipCreatingNamespace)}\" parameter is set to false.");
            }

            var nsLabels = new Dictionary<string, object>
            {
                ["appmesh.k8s.aws/sidecarInjectorWebhook"] = "enabled",
                ["mesh"] = this.AppMeshName
            };

            if (this.AddIngressGatewayToNs)
                nsLabels.Add("gateway", this.IngressGatewayName);

            meshedNsDependency = eksCluster.AddNamespaceIfNecessary(this.MeshedNamespace, nsLabels);
            return meshedNsDependency;
        }

        private KubernetesManifest AddAppMesh(ICluster eksCluster, HelmChart appMeshController)
        {
            KubernetesManifest mesh = eksCluster.AddAppMesh(this.AppMeshName);
            if (appMeshController != null)
                mesh.Node.AddDependency(appMeshController);
            return mesh;
        }

        private HelmChart GetAppMeshController(ICluster eksCluster)
            => this.SkipAppMeshControllerInstallation ?
                null : eksCluster.AddAppMeshController(this.AppMeshControllerNamespace, this.TraceWithXRayOnAppMesh);

        private HelmChart GetLbController(ICluster eksCluster)
            => this.SkipLbControllerInstallation ? 
                null : eksCluster.AddAwsLoadBalancerController(this.LbControllerNamespace);

        private ICluster GetEksCluster()
            => Cluster.FromClusterAttributes(this, "EksCluster", new ClusterAttributes
            {
                ClusterName = this.EksClusterName,
                Vpc = Vpc.FromLookup(this, "EksClusterVPC", new VpcLookupOptions {VpcId = this.ExistingVpcId}),
                KubectlRoleArn = $"arn:aws:iam::{this.Account}:role/{this.KubectlRole}",
                OpenIdConnectProvider = OpenIdConnectProvider.FromOpenIdConnectProviderArn(this, "EksOidcProvider",
                    $"arn:aws:iam::{this.Account}:oidc-provider/oidc.eks.{this.Region}.amazonaws.com/id/{this.EksOidcProviderId}"
                )
            });

        private IEnumerable<string> ValidateStackArguments()
        {
            var requiredArgErrorMsgMap = new Dictionary<string, string>
            {
                [this.AppMeshName] = "A name of a new or existing App Mesh must be specified.",
                [this.EksClusterName] = "A name of an existing EKS cluster, where App Mesh and its components will be deployed, must be specified.",
                //[this.MeshedNamespace] = "A name of Kubernetes namespace for use with App Mesh must be specified.",
                [this.ExistingVpcId] = "Id on an existing VPC of the EKS cluster must be specified.",
                [this.KubectlRole] = "A name of an existing IAM role suitable for running kubectl must be specified."
            };

            return 
                from mapItem in requiredArgErrorMsgMap
                where mapItem.Key.IsNullOrBlank()
                select mapItem.Value;
        }

        private static string GetClusterName(Construct scope)
        {
            const string clusterNameParam = "EksClusterName";
            string clusterName = scope.GetCtxString(clusterNameParam);

            if (string.IsNullOrWhiteSpace(clusterName))
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
    }
}
