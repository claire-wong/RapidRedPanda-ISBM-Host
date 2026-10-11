# Getting Started

This Windows-oriented path takes you from the required infrastructure to a publication exchanged between two clients.

**Draft status:** commands and REST examples have been reviewed against source and official documentation. The Host, database installer, and live tutorial were not run during this documentation work. Runtime setup and deployment still require validation.

1. [Prerequisites](prerequisites.md): .NET, SQL Server, Azure Service Bus, and tools.
2. [Installation](installation.md): initialize the database and namespace, build, and start the Host.
3. [Configuration](configuration.md): supply connection settings and understand the relevant options.
4. [First Message](first-message.md): exchange a JSON publication using two PowerShell clients.
5. [First Request and Response](first-request-response.md): request equipment status and consume a correlated JSON response.
6. [Notification Listener](notification-listener.md): optionally observe a publication callback on the same machine.

The installation guide points to configuration at the step where it is needed. The initial tutorial uses polling, so you do not need a callback listener.

After exchanging your first message, use the [ISBM REST API reference](../api/overview.md) for channel/session contracts, publication and request/response lifecycles, notifications, discovery, payload representations, and error handling.

The current backend is SQL Server and Azure Service Bus. This guide covers REST and a Windows development setup. It provides no Docker workflow, alternative broker setup, or SOAP instructions. Linux direct deployment has not been validated for this guide.

The Host is licensed under the [MIT License](../../LICENSE).

For deployment planning, read [Security and Deployment](../operations/security-and-deployment.md), [Maintenance and Recovery](../operations/maintenance.md), and [Troubleshooting](../operations/troubleshooting.md).

New to the terminology? Read [What Is ISBM?](../introduction/what-is-isbm.md) and [Core Concepts](../introduction/core-concepts.md). Return to the [project overview](../../README.md).
