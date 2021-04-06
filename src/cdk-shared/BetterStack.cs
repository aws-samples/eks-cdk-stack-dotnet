#nullable enable
using System;
using Amazon.CDK;
using Environment = Amazon.CDK.Environment;

namespace CdkShared
{
    public class BetterStackProps: StackProps
    {
        /// <summary>
        /// Dynamic stack names allow stack to be deployed multiple times
        /// into the same AWS account/region environment.
        /// Make stack name dependent on other parameters for best results.
        /// </summary>
        public Func<Construct, string>? DynamicStackNameGenerator { get; set; }
    }

    public class BetterStack : Stack
    {
        public BetterStack(Construct scope, string? id = null, BetterStackProps? props = null) 
            : base(scope, id, InitStackProps(scope, props))
        {
        }

        private static BetterStackProps InitStackProps(Construct scope, BetterStackProps? props)
        {
            props ??= new BetterStackProps();

            props.StackName = props.StackName.BlankToNull() ??
                              scope.GetCtxString("StackName").BlankToNull() ?? 
                              props.DynamicStackNameGenerator?.Invoke(scope).BlankToNull();

            props.Env ??= new Environment
            {
                Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
                Region = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION")
            };

            return props;
        }
    }
}
