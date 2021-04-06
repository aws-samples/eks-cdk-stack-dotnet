using System;
using Amazon.CDK;
using Amazon.CDK.AWS.EKS;
using CdkShared;

namespace CdkAppMeshEksNamespace
{
    public class CdkAppMeshEksNamespaceStack : BetterStack
    {
        public string EksClusterName => GetClusterName(this);
        public string Namespace => GetNamespace(this);

        private static string GetClusterName(Construct scope)
        {
            const string clusterNameParam = "EksClusterName";
            string clusterName = scope.GetCtxString(clusterNameParam);

            if(string.IsNullOrWhiteSpace(clusterName))
                throw new Exception("EKS cluster name must be specified");

            return clusterName;
        }

        private static string GetNamespace(Construct scope)
            => scope.GetCtxString("Namespace", "default");

        private static BetterStackProps InitStackProps(BetterStackProps props)
        {
            props ??= new BetterStackProps();

            props.DynamicStackNameGenerator ??= scope => $"AppMeshNamespace--{GetClusterName(scope)}--{GetNamespace(scope)}";

            return props;
        }

        internal CdkAppMeshEksNamespaceStack(Construct scope, string id = "CdkAppMeshEksNamespaceStack", BetterStackProps props = null) 
            : base(scope, id, InitStackProps(props))
        {
            var eksCluster = Cluster.FromClusterAttributes(this, "EksCluster", new ClusterAttributes
            {
                ClusterName = this.EksClusterName
            });
        }
    }
}
