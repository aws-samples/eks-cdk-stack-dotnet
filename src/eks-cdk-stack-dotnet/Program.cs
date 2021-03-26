using Amazon.CDK;

namespace EksEtc
{
    sealed class Program
    {
        public static void Main(string[] args)
        {
            var app = new App();

            string stackName = app.GetCtxString("StackName");
            if(stackName == null)
            {
                string clusterName = app.GetCtxString("EksClusterName", "test-cluster-by-cdk");
                stackName = $"EksEtcStack--{clusterName}";
            }

            new EksEtcStack(app, "EksEtcStack", new StackProps {
                StackName = stackName,
                Env = new Environment{
                    Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
                    Region = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION")
                }
            });

            app.Synth();
        }
    }
}
