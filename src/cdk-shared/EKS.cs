using System.Linq;
using Amazon.CDK.AWS.EKS;

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
        public static KubernetesManifest AddNamespaceIfNecessary(this Cluster eksCluster, string namespaceName)
        {
            return IsStandardNamespace(namespaceName) ? null : eksCluster.AddNamespace(namespaceName);
        }
    }
}
