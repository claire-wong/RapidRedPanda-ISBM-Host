# Getting Started

This Windows-oriented path takes you from the required infrastructure to a first integration choice, then to complete REST walkthroughs when you want to exercise the Host directly.

**Draft status:** commands and REST examples have been reviewed against source and official documentation. The Host, database installer, and live tutorial were not run during this documentation work. Runtime setup and deployment still require validation.

1. [Prerequisites](prerequisites.md): .NET, SQL Server, Azure Service Bus, and tools.
2. [Installation](installation.md): initialize the database and namespace, build, and start the Host.
3. [Configuration](configuration.md): supply connection settings and understand the relevant options.
4. [Integration Options](../integration/README.md): choose a compact .NET ClientAdapter, CLI Wrapper, or direct REST starting point.
5. [Complete REST Publication Tutorial](first-message.md): exchange a JSON publication using two PowerShell clients.
6. [Complete REST Request/Response Tutorial](first-request-response.md): request equipment status and consume a correlated JSON response.
7. [Notification Listener](notification-listener.md): optionally observe a publication callback on the same machine.

The installation guide points to configuration at the step where it is needed. The initial tutorial uses polling, so you do not need a callback listener.

## Choose Your First Integration Path

Use [Integration Options](../integration/README.md) after installation and configuration to pick the smallest useful example for your application:

| Option | Choose it when |
| --- | --- |
| [.NET ClientAdapter](../integration/dotnet-clientadapter.md) | You are building a C# or .NET application and want service classes instead of hand-written HTTP calls. |
| [CLI Wrapper](../integration/cli-wrapper.md) | You want JSON command output for scripts, automation, Python `subprocess`, or agent-driven workflows. |
| [Direct REST](../integration/direct-rest.md) | You want the language-independent ISBM REST interface and are comfortable constructing HTTP requests. |

The complete PowerShell tutorials remain available when you want to create channels, open both sides of an exchange, publish, read, remove, and clean up through REST.

After exchanging your first message, use the [ISBM REST API reference](../api/overview.md) for channel/session contracts, publication and request/response lifecycles, notifications, discovery, payload representations, and error handling.

The Host uses SQL Server and Azure Service Bus. For Linux AMD64 containers, follow [Run the Host with Docker](../operations/docker-deployment.md).

The Host is licensed under the [MIT License](../../LICENSE).

For deployment planning, read [Security and Deployment](../operations/security-and-deployment.md), [Maintenance and Recovery](../operations/maintenance.md), and [Troubleshooting](../operations/troubleshooting.md).

New to the terminology? Read [What Is ISBM?](../introduction/what-is-isbm.md) and [Core Concepts](../introduction/core-concepts.md). Return to the [project overview](../../README.md).
