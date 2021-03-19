# Welcome to EKS CDK Stack C# project!

## TL;DR

* Make sure you have [this](https://dotnet.microsoft.com/download) installed.
* cd into src\eks-cdk-stack-dotnet
* Check out [installation parameters](./src/eks-cdk-stack-dotnet/cdk.json)
* Run `cdk deploy -c EksClusterName=name-your-cluster`

## Overview

This CDK stack **deploys** AWS Elastic Kubernetes Service (**EKS**) cluster to your AWS environment (current AWS account and region) with 
- **Regular EC2** instance Linux worker nodes
- **Spot EC2** instance Linux worker nodes
- **Fargate** (serverless) Linux worker nodes
- Or any **combination** of the above
- Along with optional **AWS Load Balancer Controller** (f.k.a. AWS Ingress Controller)

## Useful commands

* `dotnet build src` compile this app
* `cdk ls`           list all stacks in the app
* `cdk synth`       emits the synthesized CloudFormation template
* `cdk deploy`      deploy this stack to your default AWS account/region
* `cdk diff`        compare deployed stack with current state
* `cdk docs`        open CDK documentation

Enjoy!
