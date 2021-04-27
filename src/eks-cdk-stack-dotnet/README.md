# Welcome to AWS EKS CDK Stack C# project!

## TL;DR

* Install [CDK CLI](https://docs.aws.amazon.com/cdk/latest/guide/getting_started.html#getting_started_install) and [latest .NET runtime for your platform](https://dotnet.microsoft.com/download).
* cd into this directory
* Check out [installation parameters](./cdk.json) in cdk.json "context" section, for better idea of this stack's capabilities.
* Run `cdk deploy -c EksClusterName=name-your-cluster`

## Overview

This of this stack as of `eksctl lite`.

This CDK stack **deploys** AWS Elastic Kubernetes Service (**EKS**) cluster to your AWS environment (current AWS account and region) with 
- **Regular (on-demand) EC2** instance Linux worker nodes,
- **Spot EC2** instance Linux worker nodes,
- **Fargate** (serverless) Linux worker nodes,
- **On-demand Graviton (ARM)** instance Linux Worker nodes (when using these, please be sure to specify `nodeSelector` for pods running images incompatible with ARM CPUs),
- Or any **combination** of the above.<br/><br/>
- Along with optional **AWS Load Balancer Controller** (f.k.a. AWS Ingress Controller),
- And optional AWS Elastic Container Registry (**ECR**) repositories.

## Stack Outputs

- "EksEtcStack.DockerLoginForECR" outputs CLI command used to allow your `docker` CLI to push local images to remote AWS Elastic Container Registry (ECR) service.
- "EksEtcStack.eksclusterConfigCommandXXXXX" outputs CLI command for authorizing and configuring local `kubectl` CLI to manage remote EKS cluster.
- "EksClusterVpcId" outputs `VPC` id of the vpc where EKS cluster was deployed. (Useful as an App Mesh stack input.)
- "EksOidcConnectProviderARN" outputs an ARN of the EKS `OIDC` provider. (Last segment of the output is useful as an App Mesh stack input.)
- "KubeCtlRoleARN" outputs an ARN of IAM `Role` created for kubectl. (Last segment of the output is useful as an App Mesh stack input.)
- "EksEtcStack.eksclusterGetTokenCommandXXXXX" outputs CLI command for retrieving JSON token used for authenticating with the EKS cluster. This can be used as an alternative to the [aws-iam-authenticator](https://docs.aws.amazon.com/eks/latest/userguide/managing-auth.html).

## Limitations

* > "cdk deploy" command for this stack should be run on a system with Internet access.
Running it from an isolated subnet will fail.
* If EKS cluster is deployed into an existing VPC, the VPC must have private subnets, i.e. *default VPC will not work*.
* If you plan to use other CDK stacks for creating namespaces whose Pods will run on Fargate nodes, then please use *this* stack to add Fargate profiles for those future namespaces, rather than using other CDK stacks for adding Fargate profiles.

## Parameter Info

Hopefully, most [installation parameters](./src/eks-cdk-stack-dotnet/cdk.json) are intuitive enough to be self-explanatory, but for transparency. Values stored in cdk.json file can be overridden during deployment time using `cdk deploy -c param1=value1 -c param2=value` command.

> Please note that it's possible to update an existing EKS cluster, using different set of values, while keeping same cluster name and stack name. For example, changing node types can be done without destroying the cluster.

* "`StackName`": `null` - allows overriding default stack name. Default value is a combination of "EksEtcStack" and cluster name. Dynamic stack names allows deploying multiple EKS clusters into the same account/region environment.
* "`EcrRepoNames`": `null` - comma-delimited string with names of ECR repositories to create. Null means no ECR repositories will be created.
* "`EksClusterName`": "`awesome-cluster`" - name of EKS cluster to be created.
* "`OptionalExistingVpcId`": `null` - an ID of an existing VPC. The VPC mus have private subnets to accommodate EKS deployment. Default VPCs commonly do not have private subnets and may not be suitable for deploying EKS clusters in them. If existing VPC is not specified, new VPC will be created by this stack.
* "`K8sVersion`": "`1.19`", default version of Kubernetes to be installed. Please check [what versions are available](https://docs.aws.amazon.com/eks/latest/userguide/kubernetes-versions.html) on AWS.
* "`OnDemandInstanceType`": "`c5a.large`" - EC2 instance (VM) type, for on-demand Linux cluster node machines.
* "`OnDemandInstanceCount`": `1` - number of on-demand Linux EC2 instances in the cluster. If set to 0, no on-demand EC2 nodes will be launched.
* "`FargateNamespaces`": `null` - a comma-delimited list of K8s namespaces that will have their pods run on serverless AWS Fargate Linux nodes. Non-existing namespaces *will not be created* by this stack, but once created, their pods will be run by Fargate.
* "`SpotInstanceTypes`": "`c5.large,c5a.large,c5d.large,t3a.large,t3.large`" - comma-delimited list of EC2 instance (VMs) types used to launch spot instances as cluster nodes. The system picks any of the listed types when assigning spot instances to the cluster. Wide set of possible instance types minimizes chances that all Spot instances will be revoked at any given time.
* "`SpotInstanceCount`": `2` - number of spot instances used as cluster node,
* "`GravitonArmInstanceType`": "`t4g.large`" - type of ARM/Graviton CPU EC2 instance (VMs) to launch as cluster nodes.
* "`GravitonArmInstanceCount`": `0` - number of ARM/Graviton EC2 instances to be used as cluster nodes. Please note that having non-x86 nodes in the cluster requires that either container images are built using multi-architecture [(multi-arch) approach](https://docs.docker.com/docker-for-mac/multi-arch/) to support both ARM and x86 CPUs, or [pods are marked with appropriate annotations](https://kubernetes.io/docs/reference/labels-annotations-taints/#kubernetes-io-arch) to state their CPU type affinity.
* "`InstallAwsLbController`": `false` - determines whether AWS Load Balancer Controller will be installed on the cluster. LB controller lets outside traffic into EKS cluster via AWS ELB/ALB, allowing cluster to stay in private subnets. LB Controller is used by K8s Ingress resource and by Service resources of "LoadBalancer" type.
* "`LbControllerNamespace`": "`kube-system`" - K8s namespace where AWS LB Controller pods will be placed.
* "`ExistingIamRolesToAllowEksManagement`": `null` - comma-delimited list of IAM Roles that will be mapped to EKS "`system:masters`" group, granting IAM Roles administrator access to the EKS cluster. Please use this parameter with caution as it may introduce security risk.

## Examples 

Here's a few examples of how this stack could be used. 
> Please run commands below from the directory containing "cdk.json" file.

#### 1. Creating cost-efficient cluster with the mix of on-demand and Spot instance nodes

> Please note that Spot instances can be revoked by AWS. Having non-Spot nodes ensures having minimum compute capacity.

```sh
cdk deploy -c StackName=my-eks-stack -c OnDemandInstanceCount=2 -c SpotInstanceCount=3
```
#### 2. Creating high-performance, cost-efficient ARM/Graviton-only cluster

> AWS Graviton2 CPU based VMs - EC2 Instances - provide up to 40% price-performance advantage compared to x86/amd64 EC2 instances of the similar spec.

Your application needs to be compiled to support arm64 CPU architecture to run on AWS Graviton EC2 instances. 

> **.NET** applications can be **easily built for both x86 and ARM** CPUs using [multi-arch docker builds](https://docs.docker.com/docker-for-mac/multi-arch/), often without having to change code at all.

Container images of optional add-ons installed by this stack, like AWS LB Controller, are compatible with both x86 and ARM CPUs and can run on Graviton-only clusters.

```sh
cdk deploy -c StackName=my-eks-stack -c GravitonArmInstanceCount=3 -c OnDemandInstanceCount=0 -c SpotInstanceCount=0
```

#### 3. Creating fully serverless, cost-efficient EKS cluster

Using Fargate cluster nodes removes VM patching and maintenance overhead, at the expense of somewhat slower speed at which a new node can be added to the cluster.

```sh
cdk deploy -c StackName=my-eks-stack -c FargateNamespaces=default,kube-system,my-app-namespace -c OnDemandInstanceCount=0 -c SpotInstanceCount=0
```


## Useful commands

* `cdk deploy`      deploy this stack to your default AWS account/region
* `cdk diff`        compare deployed stack with current state
* `cdk destroy`     destroy all AWS resources previously deployed using this stack

