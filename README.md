# Welcome to EKS CDK Stack C# project!

## TL;DR

* Make sure you have [this](https://dotnet.microsoft.com/download) installed.
* cd into src\eks-cdk-stack-dotnet
* Check out [installation parameters](./src/eks-cdk-stack-dotnet/cdk.json) for better idea of this stack's capabilities.
* Run `cdk deploy -c EksClusterName=name-your-cluster`

## Overview

This CDK stack **deploys** AWS Elastic Kubernetes Service (**EKS**) cluster to your AWS environment (current AWS account and region) with 
- **Regular (on-demand) EC2** instance Linux worker nodes,
- **Spot EC2** instance Linux worker nodes,
- **Fargate** (serverless) Linux worker nodes,
- **On-demand Graviton (ARM)** instance Linux Worker nodes (when using these, please be sure to specify `nodeSelector` for pods running images incompatible with ARM CPUs),
- Or any **combination** of the above.
- Along with optional **AWS Load Balancer Controller** (f.k.a. AWS Ingress Controller)
- ...and optional AWS Elastic Container Registry (**ECR**) repositories.

## Stack Outputs

- "EksEtcStack.DockerLoginForECR" outputs CLI command used to allow your `docker` CLI to push local images to remote AWS Elastic Container Registry (ECR) service.
- "EksEtcStack.eksclusterConfigCommandXXXXX" outputs CLI command for authorizing and configuring local `kubectl` CLI to manage remote EKS cluster.
- "EksEtcStack.eksclusterGetTokenCommandXXXXX" outputs CLI command for retrieving JSON token used for authenticating with the EKS cluster. This can be used as an alternative to the [aws-iam-authenticator](https://docs.aws.amazon.com/eks/latest/userguide/managing-auth.html).

## Limitations

* > "cdk deploy" command for this stack should be run on a system with Internet access.
Running it from an isolated subnet will fail.
* If EKS cluster is deployed into an existing VPC, the VPC must have private subnets, i.e. *default VPC will not work*.

## Useful commands

* `dotnet build src` compile this app
* `cdk ls`           list all stacks in the app
* `cdk synth`       emits the synthesized CloudFormation template
* `cdk deploy`      deploy this stack to your default AWS account/region
* `cdk diff`        compare deployed stack with current state
* `cdk docs`        open CDK documentation

Enjoy!
