using Amazon.CDK;

namespace CdkAppMeshEksNamespace
{
    sealed class Program
    {
        public static void Main(string[] args)
        {
            var app = new App();
            new CdkAppMeshEksNamespaceStack(app);
            app.Synth();
        }
    }
}
