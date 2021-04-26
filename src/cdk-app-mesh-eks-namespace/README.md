# Welcome to AWS App Mesh CDK Stack C# project!

## TL;DR

* Make sure you have [this](https://dotnet.microsoft.com/download) installed.
* cd into the directory containing this file.
* Check out [installation parameters](./cdk.json) in cdk.json "context" section, for better idea of this stack's capabilities.
* Run 
```cdk deploy -c AppMeshName=name-your-mesh -c EksClusterName=existing-cluster-name -c MeshedNamespace=eks-namespace-name -c ExistingVpcId=eks-cluster-vpc-id -c KubectlRole=iam-eks-cluster-role-name -c EksOidcProviderId=oidc-id```

## Overview

Deploying App Mesh Kubernentes components on EKS is a bit complicated, especially when mixed with deploying all or some of its parts to Fargate serverless nodes.

This CDK sample stack helps **deploying App Mesh to an existing EKS cluster**. (This stack does not help with deploying actual apps to a "meshed" K8s namespaces. For that consider using [generic app Helm chart with AWS App Mesh support](https://github.com/aws-samples/app-mesh-application-helm-charts).) 

One can use the [EKS deployment stack](../../src/eks-cdk-stack-dotnet/README.md) to provision an EKS cluster and use its stack outputs as inputs to this stack.

This stack will deploy some or all of the following components:
- **App Mesh Controller**
- **App Mesh** resource
- AWS **Load Balancer Controller**
- The **namespace** that will be a part of the Mesh
- **Ingress Gateway**, along with corresponding AWS ALB, routing traffic inside the "meshed" namespace.
- K8s **ServiceAccount** for Envoy proxies.

This stack supports the case when the "meshed" namespace has its Pods - application's and Ingress/Virtual Gateway pods - running on serverless Fargate nodes.

## Stack Outputs

- "EnvoySvcAccount" - the name of the Kubernetes service account for the Envoy proxies. This account is scoped to the "meshed" namespace and is used at least by the Ingress Gateway Envoy pod. Application Envoy pods could **reuse** it to ensure Envoy has sufficient IAM permissions to do its work. It's especially useful if one runs application Pods on Fargate nodes.

## Parameter Info

* "`StackName`": `null` - allows overriding default stack name. If not specified, the default value is a combination of "AppMeshNamespace", cluster name, and the name of the "meshed" K8s namespace. Dynamic stack names allow creating multiple "meshed" namespaces using this stack.
* "`AppMeshName`": "`sample-cdk-app-mesh`" - the name of the App Mesh.
* "`SkipCreatingAppMesh`": `false` - If false, the stack will create AWS App Mesh resource. If true, the stack will assume that App Mesh resource already exists.
* "`EksClusterName`": `null` - the name of an existing EKS cluster to which App Mesh functionality will be added.
* "`MeshedNamespace`": "`default`" - the name of the K8s namespace that will be made a part of the App Mesh. This doc refers to this namespace as "meshed" namespace. This stack can be used multiple times for the same cluster to add App Mesh support for multiple K8s namespaces.
* "`ExistingVpcId`": `null` - the id of an existing VPC in which EKS cluster is deployed. One can use the "EksClusterVpcId" output of the [EKS cluster deployment stack](../eks-cdk-stack-dotnet/README.md) to supply value for this parameter.
* "`KubectlRole`": `null` - IAM role name that has sufficient rights to run kubectl. One can use the *last segment* of the "KubeCtlRoleARN" output of the [EKS cluster deployment stack](../eks-cdk-stack-dotnet/README.md) to supply value for this parameter.
* "`EksOidcProviderId`": `null` - an of OIDC provider created for the EKS cluster. One can use the *last segment* of the "EksOidcConnectProviderARN" output of the [EKS cluster deployment stack](../eks-cdk-stack-dotnet/README.md) to supply value for this parameter.
* "`MeshedNamespaceIsOnFargate`": `true` - this setting affects how outside traffic is routed to the Ingress Gateway Envoy pods from ALB. If set to true, it tells the ALB to send traffic straight to Pod IP addresses, as required by Fargate. If false, traffic is sent to cluster EC2 node IP addresses, as required by the non-Fargate case.
* "`SkipCreatingNamespace`": `false` - if false, "meshed" namespace will be created in the EKS cluster. If true, the stack will assume the namespace that needs to be "meshed" already exists.
* "`AddIngressGatewayToNs`": `true` - if true, AWS Ingress Gateway, implementing App Mesh Virtual Gateway resource, will be installed in the "meshed" namespace.
* "`IngressGatewayNameOverride`": `null` - if specified, overrides system-generated name for the Ingress Gateway deployed by the stack.
* "`SkipLbControllerInstallation`": `true` - if true, the stack assumes that cluster already has AWS LB Controller installed on it. If false, AWS LB Controller will be added to the cluster. AWS LB Controller is used by the Ingress Gateway.
* "`SkipAppMeshControllerInstallation`": `false` - if false, the stack will install AWS App Mesh Controller on the cluster. If true, the stack assumes that AWS App Mesh Controller is already installed on the cluster.
* "`LbControllerNamespace`": "`kube-system`" - the name of the K8s namespaces where AWS LB Controller will be installed, if AWS LB Controller will be installed by this stack.
* "`AppMeshControllerNamespace`": "`appmesh-system`" - the name of the K8s namespace where AWS App Mesh controller will be installed, if App Mesh Controller will be installed by this stack.
* "`TraceWithXRayOnAppMesh`": `true` - if true, AWS X-Ray observability support will be added if App Mesh Controller is going to be installed by this stack.
* "`EnvoyServiceAccountName`": "`envoy-svc-account`" - the name of the K8s ServiceAccount that will be created for running Envoy containers. The service account is scoped to the "meshed" namespace.
* "`EnvoyServiceAccountManagedPolicies`": "`AWSAppMeshEnvoyAccess,AWSCloudMapDiscoverInstanceAccess,AWSXRayDaemonWriteAccess,CloudWatchLogsFullAccess,AWSCloudMapFullAccess,AWSAppMeshFullAccess`" - IAM managed policies assigned to the K8s ServiceAccount used for running Envoy pods.
* "`IngressGatewayHelmChartUrl`": "`https://github.com/aws-samples/aws-app-mesh-helm-chart/raw/main/packaged-charts/eks-app-mesh-gateway-0.1.0.tgz`" - URL of the Helm chart used to install Ingress Gateway in the "meshed" namespace.
* "`IngressGatewayImageTag`": "`v1.17.2.0-prod`" - the container image tag of the Envoy to be used. Latest version information can be found https://docs.aws.amazon.com/app-mesh/latest/userguide/envoy.html.

## Useful commands

* `cdk deploy`      deploy this stack to your default AWS account/region
* `cdk diff`        compare deployed stack with current state
* `cdk destroy`     destroy all AWS resources previously deployed using this stack