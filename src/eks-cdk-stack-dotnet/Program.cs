using Amazon.CDK;

namespace EksEtc
{
    sealed class Program
    {
        public static void Main(string[] args)
        {
            var app = new App();
            new EksEtcStack(app, "EksEtcStack", new StackProps {
                StackName = app.GetCtxString("StackName"),
                Env = new Environment{
                    Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
                    Region = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION")
                }
            });

            app.Synth();
        }
    }
}
