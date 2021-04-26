# CDK stacks for deploying AWS EKS clusters and AWS App Mesh

Welcome! This repository contains an example of cross-platform (Linux/Windows/MacOS) .NET solution comprising two parameterized CDK stack projects:
- [First](./src/eks-cdk-stack-dotnet/README.md), deploying AWS Elastic **Kubernetes** Service (EKS) cluster.
- [Second](./src/cdk-app-mesh-eks-namespace/README.md), deploying **AWS App Mesh** components to en existing EKS cluster.

Although each stack works well independently, EKS stack provides useful outputs for the App Mesh stack, making deploying an App Mesh to an EKS cluster a very easy proposition. Please click links above for details on how to use each stack.

## Security

See [CONTRIBUTING](CONTRIBUTING.md#security-issue-notifications) for more information.

## License

This library is licensed under the MIT-0 License. See the [LICENSE](./LICENSE) file.
