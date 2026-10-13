# Installation

This is a source-reviewed Windows development workflow. It has not been executed end to end for this draft. Use a development database and namespace, and stop to investigate any failing step before continuing.

## Obtain the Source

Obtain an authorized clone or source archive of RapidRedPanda ISBM Host and open PowerShell 7 in the repository root. A packaged binary is not assumed.

The relevant layout is:

```text
RapidRedPanda.ISBM.Host/
  RapidRedPanda.ISBM.Host.slnx
  RapidRedPanda-ISBM-Host/
    RapidRedPanda.ISBM.Host.csproj
    Program.cs
  Database/Scripts/Install/
    001_CreateDatabase.sql
```

Confirm the [prerequisites](prerequisites.md). The commands below use repository-relative paths and build the Host project directly rather than the solution's test project.

## Initialize SQL Server

Use [the consolidated installation script](../../RapidRedPanda.ISBM.Host/Database/Scripts/Install/001_CreateDatabase.sql). Do not install by running the individual development migrations.

1. In SSMS, connect to the intended SQL Server instance with an installation identity authorized to create the database and schema, or use a DBA-prepared empty database.
2. Open `RapidRedPanda.ISBM.Host/Database/Scripts/Install/001_CreateDatabase.sql`.
3. Review the `@DatabaseName` declaration. The script's actual default is `RapidRedPandaISBM`; change the declaration in your installation copy if you need another database name, then use the same name in the Host connection string.
4. Select `master` as the initial query database. Execute the complete script normally, outside a caller-created transaction. SQLCMD Mode is not required.
5. Review the Messages tab and refresh the database in Object Explorer. Continue only if installation completed without errors.

The script creates the named database if absent, using SQL Server's configured default data and log locations. If it already exists, it requires the database to be online, multi-user, and writable. It switches database context inside its dynamic SQL and installs the schema in a transaction. Database creation occurs before that transaction; a schema failure can leave a newly created empty database.

The installer must have database-creation permission when creating a database, and permission to create schema objects and write initialization records. Consult Microsoft's [CREATE DATABASE permission guidance](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-database-transact-sql?view=sql-server-ver17) and your DBA. Runtime access is described in [Prerequisites](prerequisites.md#sql-server).

### Verify the Schema

Open a **new query in the installed database**; the original query window may still show `master`. These are read-only checks for the reader to execute:

```sql
SELECT DB_NAME() AS CurrentDatabase;

SELECT VersionNumber, AppliedUtc, ScriptName
FROM dbo.SchemaVersions
ORDER BY AppliedUtc, VersionNumber;

SELECT name
FROM sys.tables
WHERE schema_id = SCHEMA_ID(N'dbo')
ORDER BY name;
```

A fresh installation should contain schema-version entries including `PhaseOne-ChannelPersistence` and `NotificationCleanupIndexes-001`, plus tables for channels, security tokens, publication and request sessions, messages, responses, and notifications. These are expectations from the script, not recorded output from a live installation.

**Existing database caution:** if the `PhaseOne-ChannelPersistence` marker already exists, the script returns without installing later objects. A final success message alone therefore does not establish that an older database has the current schema. This script is a fresh installer, not a verified upgrade procedure. Do not reset an existing database or run development migrations as a workaround; seek maintainer guidance.

Reset and purge scripts under `Database/Scripts/Maintenance/` can remove data. They are not installation steps and should not be run as part of this tutorial.

## Prepare Azure Service Bus

1. Prepare an Azure subscription and create or select a development Service Bus namespace using **Standard or Premium**.
2. In the namespace's shared access policies, create a dedicated Host policy with **Manage** rights. This includes Send and Listen and permits the entity administration performed by the Host.
3. Obtain that policy's namespace connection string through your approved secret-handling process. Use a namespace connection string without an `EntityPath` suffix.
4. Store it as `AzureServiceBus:ConnectionString` using [Configuration](configuration.md).

Use the namespace-creation portion of Microsoft's [setup guide](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-quickstart-topics-subscriptions-portal). Its manual topic/subscription examples are not needed for RapidRedPanda. See [shared access policy rights](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-sas) for permissions.

### Broker Entities Managed by the Host

- Creating an ISBM channel creates its broker topic automatically.
- Opening a consumer subscription session creates its broker subscription and the `ISBMTopicSelection` rule, and removes the default catch-all rule.
- Channel topic names follow `isbm-ch-` plus the channel's generated internal GUID as 32 hexadecimal characters.
- Session subscription names follow `isbm-css-` plus the session GUID in the same format. These are broker identifiers, not the client-supplied ISBM topic label.
- Closing the corresponding receiving session deletes its subscription; deleting a channel deletes its broker topic.

With default options, a background channel-topic synchronization pass begins when the Host starts and repeats every 300 seconds. It checks SQL channel bindings and recreates missing topics. This does not recover lost broker messages or recreate all session subscriptions.

Subscription reconciliation occurs when opening a consumer publication subscription: it preserves open known subscriptions and deletes generated subscriptions with closed or missing SQL session records. It is not a comprehensive startup restore of broker state.

The same namespace credentials must remain available during normal operation because these administrative operations continue at runtime. Permission and connectivity failures can appear as `500 OperationFault` during channel/session operations or as background-service errors. Review Host logs; do not paste keys or connection strings into an issue report.

## Configure the Host

Follow [Configuration](configuration.md) in the same terminal you will use to start the Host. Supply the SQL connection string and Azure namespace connection string without placing credentials in the repository.

For the first-message workflow, you can keep the reviewed default synchronization and notification options. The tutorial omits a listener URL and polls for messages.

## Build and Start a Development Instance

From the repository root, prepare and trust the local development certificate:

```powershell
dotnet dev-certs https --trust
```

Approve the operating system's trust prompt when appropriate. This is for local development, not a production certificate. See Microsoft's [development certificate command](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs).

Build and start:

```powershell
$hostProject = 'RapidRedPanda.ISBM.Host/RapidRedPanda-ISBM-Host/RapidRedPanda.ISBM.Host.csproj'

dotnet build $hostProject --configuration Debug
if ($LASTEXITCODE -ne 0) { throw 'Host build failed.' }

$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_ENVIRONMENT = 'Development'

dotnet run --project $hostProject --configuration Debug --no-build --no-launch-profile -- --urls 'https://localhost:7443'
```

The build may restore NuGet packages and requires access to the configured package sources. These commands are instructions for the reader; they were not run during drafting.

The explicit URL avoids relying on a workstation's launch profile. Port 7443 is an example loopback port; if occupied, choose a free port and use that HTTPS URL consistently in the client examples. Check the actual listening URL and startup errors in the console. Keep this terminal running while using the clients.

Swagger is enabled only in Development. It is optional and is not needed for the tutorial.

## Verify the REST Endpoint

In another PowerShell 7 window:

```powershell
$hostBaseUrl = 'https://localhost:7443'
$discovery = Invoke-RestMethod -Method Get -Uri "$hostBaseUrl/isbm/2.0/configuration/supported-operations" -ErrorAction Stop
$discovery
```

The controller defines a `200 OK` JSON response with supported-operation information, including disabled XML/JSON filtering and Basic REST authentication. This endpoint confirms that the REST layer responds; it does **not** prove SQL access or broker connectivity. The [Complete REST Publication Tutorial](first-message.md) exercises those paths.

If TLS fails, confirm the certificate is valid and trusted and that you are connecting to its hostname. Do not disable certificate validation or send channel credentials to an HTTP URL. If startup option validation fails, compare your effective settings with [Configuration](configuration.md).

## Publish an Application Folder

For a framework-dependent application folder, run from the repository root:

```powershell
$hostProject = 'RapidRedPanda.ISBM.Host/RapidRedPanda-ISBM-Host/RapidRedPanda.ISBM.Host.csproj'
dotnet publish $hostProject --configuration Release --self-contained false --output './artifacts/host'
if ($LASTEXITCODE -ne 0) { throw 'Host publish failed.' }
```

On a Windows machine with the required runtimes, provide configuration to the account that will run the app, change into the published folder, and run `dotnet ./RapidRedPanda.ISBM.Host.dll`. For a local development smoke check, the same development certificate/environment setup can be used with `--urls 'https://localhost:7443'`. Build output and this launch path have not been exercised for the draft.

A deployment environment needs its own runtime account, protected configuration, valid HTTPS endpoint/certificate, process supervision, and operational validation. Use Production for deployed environments; development user secrets and a workstation development certificate are not a deployment plan. The Host does not configure a production certificate or forwarded-header handling itself. Its security discovery settings also require review, as explained in [Configuration](configuration.md#security-behavior).

The published project includes database install and maintenance SQL files. Publishing or starting the Host does not execute them automatically.

Next: [Complete REST Publication Tutorial](first-message.md). Settings: [Configuration](configuration.md). Index: [Getting Started](README.md).
