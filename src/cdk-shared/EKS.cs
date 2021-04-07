using System.Collections.Generic;
using System.Linq;
using Amazon.CDK.AWS.EKS;
using Amazon.CDK.AWS.IAM;

namespace CdkShared
{
    public static class Eks
    {
        public static string[] DefaultK8sNamespaces => new [] { "default", "kube-system", "kube-public" };

        public static bool IsStandardNamespace(string namespaceName)
            => DefaultK8sNamespaces.Contains(namespaceName);

        /// <summary>
        /// Creates K8s namespace if namespace name is not "default", "kube-system", or "kube-public"
        /// </summary>
        /// <param name="eksCluster"></param>
        /// <param name="namespaceName"></param>
        /// <returns></returns>
        public static KubernetesManifest AddNamespaceIfNecessary(this ICluster eksCluster, string namespaceName)
        {
            return IsStandardNamespace(namespaceName) ? null : eksCluster.AddNamespace(namespaceName);
        }


        /// <summary>
        /// Installs AWS App Mesh Controller - an add-on integrating Kubernetes cluster with AWS App Mesh.
        /// </summary>
        /// <param name="eksCluster"></param>
        /// <param name="appMeshControllerNamespace">K8s namespace where AWS App Mesh controller will be installed</param>
        /// <param name="traceWithXRayOnAppMesh">Set to true to enable AWS X-Ray side-car container </param>
        /// <returns></returns>
        public static HelmChart AddAppMeshController(this ICluster eksCluster, 
            string appMeshControllerNamespace = "appmesh-system",
            bool traceWithXRayOnAppMesh = true)
        {
            // Create K8s namespace if installing the controller in non-default/kube-system namespace
            KubernetesManifest amcNamespace = eksCluster.AddNamespaceIfNecessary(appMeshControllerNamespace);

            // Create K8s service account for the controller. This service account will be mapped to AWS IAM Roles
            // letting controller interface with AWS services to do its job.
            ServiceAccount svcAccount = eksCluster.AddServiceAccount("aws-app-mesh-controller-svc-account", new ServiceAccountOptions
            {
                Name = "appmesh-controller",
                Namespace = appMeshControllerNamespace
            });
            svcAccount.Role.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("AWSCloudMapFullAccess"));
            svcAccount.Role.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("AWSAppMeshFullAccess"));

            if (amcNamespace != null)
                svcAccount.Node.AddDependency(amcNamespace);

            // Set Helm chart parameters
            var chartValues = new Dictionary<string, object>
            {
                ["region"] = eksCluster.Stack.Region,
                ["serviceAccount"] = new Dictionary<string, object>
                {
                    ["create"] = false,
                    ["name"] = svcAccount.ServiceAccountName
                }
            };

            // If X-Ray tracing enabled via parameters, turn X-Ray on via helm chart params.
            if (traceWithXRayOnAppMesh)
                chartValues.Add("tracing", new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["provider"] = "x-ray"
                });

            // Run the Helm Chart installing App Mesh controller
            // Note that Helm release "app-mesh-by-cdk" will be accessible using "helm list" command
            HelmChart chart = eksCluster.AddHelmChart("appmesh-controller", new HelmChartProps
            {
                Repository = "https://aws.github.io/eks-charts",
                Chart = "appmesh-controller",
                Release = "app-mesh-by-cdk",
                Namespace = appMeshControllerNamespace,
                Values = chartValues
            });

            chart.Node.AddDependency(svcAccount);
            return chart;
        }


        /// <summary>
        /// Installs AWS Load Balancer Controller to let outside traffic into the cluster via AWS Application Load Balancer.
        /// </summary>
        /// <param name="eksCluster"></param>
        /// <param name="lbControllerNamespace"></param>
        /// <returns></returns>
        public static HelmChart AddAwsLoadBalancerController(this ICluster eksCluster, 
            string lbControllerNamespace = "kube-system")
        {
            KubernetesManifest lbcNamespace = eksCluster.AddNamespaceIfNecessary(lbControllerNamespace);
            ServiceAccount svcAccount = eksCluster.CreateLbControllerServiceAccount(lbControllerNamespace, lbcNamespace);

            // Runs Helm chart installing AWS LB controller.
            // The manifest will be accessible via "helm list" on the system where "cdk deploy" was run.
            HelmChart chart = eksCluster.AddHelmChart("aws-load-balancer-controller-chart", new HelmChartOptions
            {
                Repository = "https://aws.github.io/eks-charts",
                Chart = "aws-load-balancer-controller",
                Release = "aws-lb-controller-by-cdk",
                Namespace = lbControllerNamespace,
                Values = new Dictionary<string, object>
                {
                    ["fullnameOverride"] = "aws-lb-controller",
                    ["clusterName"] = eksCluster.ClusterName,
                    ["serviceAccount"] = new Dictionary<string, object>
                    {
                        ["create"] = false,
                        ["name"] = svcAccount.ServiceAccountName,
                    },
                    ["vpcId"] = eksCluster.Vpc.VpcId,
                    ["region"] = eksCluster.Stack.Region
                }
            });

            chart.Node.AddDependency(svcAccount);

            return chart;
        }

        /// <summary>
        /// Creates K8s service account for AWS LB Controller to run under.
        /// Attaches appropriate IAM policies to the service account to let AWS LB Controller do its job.
        /// </summary>
        /// <param name="eksCluster"></param>
        /// <param name="k8sNamespace">K8s namespace where ALB Ingress Controller will be installed</param>
        /// <returns></returns>
        private static ServiceAccount CreateLbControllerServiceAccount(this ICluster eksCluster, string k8sNamespace, KubernetesManifest lbcNamespace)
        {
            ServiceAccount svcAccount = eksCluster.AddServiceAccount("aws-lb-controller-svc-account", new ServiceAccountOptions
            {
                Name = "aws-lb-controller",
                Namespace = k8sNamespace
            });

            if (lbcNamespace != null)
                svcAccount.Node.AddDependency(lbcNamespace);

            IEnumerable<object> lbControllerIamPolicy = DownloadLbControllerIamPolicyStatements();
            foreach (object iamPolicyJsonItem in lbControllerIamPolicy)
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
        private static IEnumerable<object> DownloadLbControllerIamPolicyStatements()
        {
            // This IAM policy is AWS-supported (https://github.com/kubernetes-sigs/aws-load-balancer-controller/blob/main/docs/install/iam_policy.json)
            // and is part of AWS-recommended routine of installing AWS LB Controller (https://docs.aws.amazon.com/eks/latest/userguide/aws-load-balancer-controller.html)
            const string iamPolicyUrl = "https://raw.githubusercontent.com/kubernetes-sigs/aws-load-balancer-controller/main/docs/install/iam_policy.json";

            string iamPolicyJson = Utils.ReadContentFromUrl(iamPolicyUrl);
            var parsedPolicy = (Dictionary<string, object>)Utils.JsonToMap(iamPolicyJson);
            var policyStatements = (IEnumerable<object>)parsedPolicy?["Statement"];
            return policyStatements;
        }

    }
}
