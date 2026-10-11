# Prerequisites

Prepare these components before following [Installation](installation.md). The instructions target a Windows development workstation and a separate Azure Service Bus namespace.

## .NET 10

Building and running from source requires the **.NET 10 SDK**. The Host project targets `net10.0` and uses the ASP.NET Core Web SDK.

For the framework-dependent published application described in the installation guide, the destination machine needs the compatible .NET 10 runtime and ASP.NET Core runtime. Installing the SDK supplies the development components too. The ordinary .NET runtime alone does not provide the web runtime.

Use Microsoft's [.NET installation guidance for Windows](https://learn.microsoft.com/en-us/dotnet/core/install/windows). Choose components for your machine's architecture. Check the SDK in your terminal:

```powershell
dotnet --list-sdks
dotnet --list-runtimes
```

The first command should list a 10.x SDK for source builds. For running the published application, check for compatible 10.x `Microsoft.NETCore.App` and `Microsoft.AspNetCore.App` runtimes.

## SQL Server

The Host uses SQL Server through `Microsoft.Data.SqlClient`. Prepare an accessible SQL Server Database Engine instance and use the consolidated SQL installation script provided by this repository.

The source does not establish a supported minimum SQL Server version or a tested edition matrix. The schema uses JSON validation, transactions, filtered indexes, and SQL Server-specific types. Confirm that your chosen engine and database configuration support the supplied script; an edition's general SQL Server compatibility is not a tested Host support claim.

For a local setup, identify the instance you installed. For a remote setup, ask the administrator for the server/instance or network address, connection policy, and database access. Do not assume a particular instance name, authentication mode, firewall configuration, or LocalDB deployment.

There are two different permission needs:

| Identity | Required access |
| --- | --- |
| Installer | Connect to the chosen instance; create the database if absent; create tables, constraints, and indexes in its `dbo` schema and insert schema-version records. |
| Host runtime | Connect to the installed database and perform the reads and writes used by the repositories: SELECT, INSERT, UPDATE, and DELETE on the Host tables. |

A DBA can pre-create the empty database to avoid granting the installer server-level database-creation privileges. Database ownership during installation is a straightforward administrative option, not a requirement for the runtime account. Have the DBA grant appropriate runtime table access separately; do not use an administrator account as the normal Host identity. Exact least-privilege grants have not been exercised in this draft.

Windows integrated authentication and SQL authentication are configuration choices. Integrated authentication uses the account running the Host, which may differ from the account running the installer. Your connection string must name the database selected in the installation script and comply with the server's certificate policy.

## Azure Service Bus

You need an Azure subscription and a **Standard or Premium** Service Bus namespace. Basic does not support the topics and subscriptions used by this Host. See Microsoft's [namespace setup guide](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-quickstart-topics-subscriptions-portal).

The current Host uses one namespace connection string for both messaging and entity administration. Prepare a dedicated namespace-level shared access policy with **Manage** rights, which include Send and Listen. A send-only or listen-only connection cannot perform the Host's topic and subscription administration. See [Service Bus shared access authorization](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-sas).

The Host creates and manages its own broker entities. Use a dedicated development namespace where possible, and follow the [Azure preparation steps](installation.md#prepare-azure-service-bus) rather than manually creating tutorial topics.

Allow the workstation's network to reach SQL Server and the namespace. Service Bus administration uses HTTPS; the registered messaging client uses its default AMQP transport. An environment allowing HTTPS alone may need additional network preparation. This guide does not establish proxy or restricted-network support.

Azure usage can incur charges. Check the current [Service Bus pricing](https://azure.microsoft.com/en-us/pricing/details/service-bus/) for your region and tier. No fixed price or promotional credit is assumed.

## Tools

- **PowerShell 7** is required for this tutorial's HTTP status-handling examples. It is a client-tool requirement, not a Host runtime dependency. [Install PowerShell on Windows](https://learn.microsoft.com/en-us/powershell/scripting/install/install-powershell-on-windows).
- **SQL Server Management Studio (SSMS)** is the database tool used in this guide. An equivalent SQL client that supports the script's batches is also possible. [Install SSMS](https://learn.microsoft.com/en-us/ssms/install/install).
- **Git** is optional if you obtain an authorized source archive instead of cloning.
- **Visual Studio** or another editor is optional; the guide builds the Host project with the .NET CLI. An IDE must support the project's .NET 10 target.
- **A trusted local HTTPS development certificate** is needed for the loopback endpoint used in the guide. Installation explains how to prepare it without disabling client certificate checks.

Next: [Installation](installation.md). Index: [Getting Started](README.md).
