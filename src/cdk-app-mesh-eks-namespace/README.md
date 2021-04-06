# Welcome to your CDK C# project!

This CDK project (stack) makes an EKS namespace suitable for AWS App Mesh.
It does the following:
- Creates a namespace if it does not exist
- Labels it for usage by the App Mesh
- Creates K8s service account for Envoy, mapped to an appropriate IAM role.
- Optionally creates an App Mesh
- Optionally deploys AWS Ingress Gateway (App Mesh Virtual Gateway) mapped to an AWS Elastic Load Balancer.

The `cdk.json` file tells the CDK Toolkit how to execute your app.

It uses the [.NET Core CLI](https://docs.microsoft.com/dotnet/articles/core/) to compile and execute your project.

## Useful commands

* `dotnet build src` compile this app
* `cdk ls`           list all stacks in the app
* `cdk synth`       emits the synthesized CloudFormation template
* `cdk deploy`      deploy this stack to your default AWS account/region
* `cdk diff`        compare deployed stack with current state
* `cdk docs`        open CDK documentation

Enjoy!
