using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.EKS;

#nullable enable

namespace CdkShared
{
    public static class Resource
    {
        #region "cdk.json" file (context) parameters (and "-c" command line args)

        // Context info: https://docs.aws.amazon.com/cdk/latest/guide/context.html

        public static TRes? Create<TRes>(this Construct scope, string name, object? props)
            where TRes : Construct
            =>
                (TRes?) Activator.CreateInstance(typeof(TRes), scope, name, props);

        public static Nullable<T> GetCtxValue<T>(this Construct scope, string name) where T : struct
        {
            T defaultValue = default(T);
            T retVal = scope.GetCtxValue(name, defaultValue);

            if (retVal.Equals(defaultValue))
                return null;
            return retVal;
        }

        public static T GetCtxValue<T>(this Construct scope, string name, T defaultValue) where T : struct
        {
            object retVal = scope.Node.TryGetContext(name);
            if (retVal == null)
                return defaultValue;
            if (retVal is string)
                return string.IsNullOrEmpty(retVal as string) ? defaultValue :  (T) Convert.ChangeType(retVal, typeof(T));
            return (T) retVal;
        }

        public static IEnumerable<string>? GetCtxStrings(this Construct scope, string name, params char[] delimiters)
        {
            string? delimited = scope.GetCtxString(name);
            return delimited?.StringToCollection(delimiters);
        }

        public static IEnumerable<string> GetCtxStrings(this Construct scope, string name, string defaultValue,
            params char[] delimiters)
            =>
                scope.GetCtxString(name, defaultValue).StringToCollection(delimiters);

        private static IEnumerable<string> StringToCollection(this string delimited, params char[] delimiters)
        {
            if (!delimiters.Any())
                delimiters = new[] {',', ';'};
            return delimited.Split(delimiters).Select(s => s.Trim());
        }

        public static string GetCtxString(this Construct scope, string name, string defaultValue)
        {
            string retVal = scope.GetCtx<string>(name, defaultValue);
            return string.IsNullOrEmpty(retVal) ? defaultValue : retVal;
        }

        public static string? GetCtxString(this Construct scope, string name)
        {
            string? retVal = scope.Node.TryGetContext(name) as string;
            return string.IsNullOrEmpty(retVal) ? (string?) null : retVal;
        }

        public static T? GetCtx<T>(this Construct scope, string name) where T : class, new()
        {
            T defaultValue = new T();
            T retVal = scope.GetCtx(name, defaultValue);

            if (retVal == defaultValue)
                return null;
            return retVal;
        }

        public static T GetCtx<T>(this Construct scope, string name, T defaultValue) where T : class
        {
            object retVal = scope.Node.TryGetContext(name);
            if (retVal == null)
                return defaultValue;
            return (T) retVal;
        }

        #endregion "cdk.json" file (context) parameters (and "-c" command line args)

        #region CfnParameter helpers

        public static CfnParameter Param(this Construct scope, string paramName, string defaultValue,
            string description, string type = "String")
            =>
                new CfnParameter(scope, paramName, new CfnParameterProps
                {
                    Default = defaultValue,
                    Description = description,
                    Type = string.IsNullOrWhiteSpace(type) ? "String" : type
                });

        public static CfnParameter ListParam(this Construct scope, string paramName, string defaultValue,
            string description)
            =>
                scope.Param(paramName, defaultValue, description, "CommaDelimitedList");

        public static CfnParameter Param(this Construct scope, string paramName, double defaultValue,
            string description)
            =>
                new CfnParameter(scope, paramName, new CfnParameterProps
                {
                    Default = defaultValue,
                    Description = description,
                    Type = "Number"
                });

        #endregion CfnParameter helpers


        public static Nodegroup? AddNodeGroup(this Cluster eksCluster, string id, string instanceType,
            double instanceCount,
            CapacityType capacityType = CapacityType.ON_DEMAND, NodegroupAmiType amiType = NodegroupAmiType.AL2_X86_64)
            =>
                AddNodeGroup(eksCluster, id, instanceType.ToEnumerable(), instanceCount, capacityType, amiType);

        public static Nodegroup? AddNodeGroup(this Cluster eksCluster, string id, IEnumerable<string>? instanceTypes,
            double instanceCount,
            CapacityType capacityType = CapacityType.ON_DEMAND, NodegroupAmiType amiType = NodegroupAmiType.AL2_X86_64)
        {
            if (instanceCount < 1)
                return null;

            if (instanceTypes?.FirstOrDefault() == null)
                throw new ArgumentException($"Argument \"{nameof(instanceTypes)}\" must be specified.");

            return eksCluster.AddNodegroupCapacity(id, new NodegroupOptions
            {
                CapacityType = capacityType,
                InstanceTypes = instanceTypes?.Select(instanceTypeString => new InstanceType(instanceTypeString))
                    .ToArray(),
                MinSize = instanceCount,
                AmiType = amiType,
            });
        }

        public static KubernetesManifest AddNamespace(this ICluster eksCluster, string k8sNamespace, Dictionary<string, object>? labels = null)
        {
            var resourceMetadata = new Dictionary<string, object>
            {
                ["name"] = k8sNamespace
            };

            if (labels != null && labels.Count > 0)
                resourceMetadata.Add("labels", labels);

            var manifest = new Dictionary<string, object>
            {
                ["apiVersion"] = "v1",
                ["kind"] = "Namespace",
                ["metadata"] = resourceMetadata
            };

            return eksCluster.AddManifest($"{k8sNamespace}-ns", manifest);
        }

        public static KubernetesManifest AddAppMesh(this ICluster eksCluster, string appMeshName, Dictionary<string, object>? labels = null)
        {
            var resourceMetadata = new Dictionary<string, object>
            {
                ["name"] = appMeshName
            };

            if (labels != null && labels.Count > 0)
                resourceMetadata.Add("labels", labels);

            var manifest = new Dictionary<string, object>
            {
                ["apiVersion"] = "appmesh.k8s.aws/v1beta2",
                ["kind"] = "Mesh",
                ["metadata"] = resourceMetadata,
                ["spec"] = new Dictionary<string,object>
                {
                    ["namespaceSelector"] = new Dictionary<string, object>
                    {
                        ["matchLabels"] = new Dictionary<string, object>
                        {
                            ["mesh"] = appMeshName
                        }
                    }
                }
            };

            return eksCluster.AddManifest($"app-mesh-{appMeshName}", manifest);
        }

        public static CfnOutput Output(this Construct scope, string id, string value, string description)
            => new CfnOutput(scope, id, new CfnOutputProps {Value = value, Description = description});
    }
}