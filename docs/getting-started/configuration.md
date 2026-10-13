# Configuration

Configure the Host process before following [Installation](installation.md). The configuration names below come from startup registration, repository access, and the current option classes.

## Required Connection Settings

| Key | Required | Type and example shape | Purpose |
| --- | --- | --- | --- |
| `ConnectionStrings:ISBMHost` | Yes for database operations | String: `Server=<SQL_SERVER>;Database=<INSTALLED_DATABASE>;Integrated Security=True;Encrypt=True;TrustServerCertificate=False` | Connects the Host to its installed SQL Server database. |
| `AzureServiceBus:ConnectionString` | Yes; validated at startup | String: `Endpoint=sb://<NAMESPACE>.servicebus.windows.net/;SharedAccessKeyName=<HOST_POLICY>;SharedAccessKey=<SECRET>` | Supplies namespace credentials for messaging and entity administration. |

These are placeholder shapes, not executable connection strings. Choose integrated or SQL authentication according to your server policy. For SQL authentication, supply the appropriate `User ID` and `Password` in the protected connection string. Use the database name chosen in the installer. SQL certificate validation must match your server's certificate configuration; the examples do not bypass trust checks.

Use a namespace-level Service Bus connection string with Manage rights, without an `EntityPath` restriction. Startup rejects an empty Service Bus value, but accepting a string does not establish that its credentials, tier, or network access work.

## Environment Variables

ASP.NET Core configuration maps double underscores in environment variable names to colons. Supply these to the process that starts the Host:

- `ConnectionStrings__ISBMHost`
- `AzureServiceBus__ConnectionString`

In the **Host terminal**, the following prompts keep literal credentials out of the commands you enter:

```powershell
$sqlConnectionInput = Read-Host 'SQL connection string for the installed Host database' -AsSecureString
$busConnectionInput = Read-Host 'Service Bus namespace connection string for the Host policy' -AsSecureString

$env:ConnectionStrings__ISBMHost = [pscredential]::new('unused', $sqlConnectionInput).GetNetworkCredential().Password
$env:AzureServiceBus__ConnectionString = [pscredential]::new('unused', $busConnectionInput).GetNetworkCredential().Password

Remove-Variable sqlConnectionInput, busConnectionInput
```

The variables apply to that terminal and its child processes, not other already-open terminals. They remain plaintext process environment values after assignment. Restrict access to the runtime account and terminal; do not print the variables or include them in logs.

After stopping the Host, remove them from the terminal when no longer needed:

```powershell
Remove-Item Env:ConnectionStrings__ISBMHost, Env:AzureServiceBus__ConnectionString -ErrorAction SilentlyContinue
```

For deployment, inject configuration through the hosting environment's approved secret-handling mechanism. This Host does not establish managed-identity authentication for Service Bus or an automatic external vault provider.

## Development User Secrets

The Host project already contains a `UserSecretsId`; no project edit or `user-secrets init` is needed. As an alternative for development, after the environment-variable prompts above, store those values through the CLI's standard input:

```powershell
$hostProject = 'RapidRedPanda.ISBM.Host/RapidRedPanda-ISBM-Host/RapidRedPanda.ISBM.Host.csproj'
$developmentSettings = @{
    'ConnectionStrings:ISBMHost' = $env:ConnectionStrings__ISBMHost
    'AzureServiceBus:ConnectionString' = $env:AzureServiceBus__ConnectionString
}
$developmentSettings | ConvertTo-Json | dotnet user-secrets set --project $hostProject
if ($LASTEXITCODE -ne 0) { throw 'Saving development secrets failed.' }
Remove-Variable developmentSettings
```

Run this from the repository root. The values are supplied on standard input instead of as literal command arguments. User secrets live outside the repository but are not encrypted and are intended only for development. See Microsoft's [user secrets guidance](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0).

The Host uses `WebApplication.CreateBuilder`. Its normal application configuration includes base settings, environment-specific settings, development user secrets, environment variables, and command-line values. Environment variables override the corresponding settings-file and user-secret values; command-line configuration can override them again. See [ASP.NET Core configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-10.0).

The documented `--no-launch-profile` option avoids launch-profile overrides but still allows `appsettings.Development.json` to load in Development if present. Check effective configuration without exposing its secret values.

## Environment and HTTPS

| Setting | Required or optional | Safe example | Notes |
| --- | --- | --- | --- |
| `DOTNET_ENVIRONMENT` and `ASPNETCORE_ENVIRONMENT` | Set explicitly for the documented workflow | `Development` locally; `Production` for deployment | Keep both values consistent. Development enables Swagger and user secrets. |
| `--urls` or `ASPNETCORE_URLS` | Explicit endpoint recommended | `https://localhost:7443` for this local guide | A valid server certificate is still needed. The command-line example in installation sets the endpoint. |
| `AllowedHosts` | Optional Host-header restriction | `localhost` | Use allowed host names for the environment; this is not channel authentication or a network firewall. |
| `Logging:LogLevel:Default` | Optional | `Information` | Controls logging verbosity. Avoid sharing secret-bearing diagnostics. |

Use a trusted ASP.NET Core development certificate for the local endpoint. Production requires appropriate certificates and hosting configuration. See [Kestrel HTTPS endpoint configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0).

## Channel Topic Synchronization

These options have reviewed defaults and need no override for the first-message walkthrough.

| Key | Type | Default/example | Constraint and purpose |
| --- | --- | --- | --- |
| `ChannelTopicSynchronization:Enabled` | Boolean | `true` | Enables the background pass that recreates missing channel topics. |
| `ChannelTopicSynchronization:IntervalSeconds` | Integer | `300` | Must be at least 30, including when disabled; controls the repeat interval. |

Synchronization cannot recover lost broker messages. It is distinct from subscription cleanup performed when a consumer subscription is opened.

## Notifications

The tutorial omits `listenerUrl` and polls through REST. No callback service or notification-setting changes are required for that workflow.

A listener URL, when used, is a field of the relevant session-opening request, not a global Host configuration key. It must be an absolute HTTP or HTTPS URL, have no user information or fragment, have no surrounding whitespace, and normalize to at most 2048 characters. A listener must implement the callback contract and be reachable by the Host. Detailed callback setup is outside this first-message walkthrough.

The shared dispatcher uses the `PublicationNotifications` section for publication, request, and response availability notifications. Its defaults are:

| Key suffix under `PublicationNotifications:` | Type | Default/example | Purpose | Valid range or relationship |
| --- | --- | --- | --- | --- |
| `Enabled` | Boolean | `true` | Enable dispatcher processing. | Boolean. |
| `BatchSize` | Integer | `20` | Limit notifications leased per batch. | 1-500. |
| `MaxConcurrency` | Integer | `4` | Limit concurrent callback deliveries. | 1-100. |
| `IdlePollIntervalSeconds` | Integer | `5` | Wait between polls when idle. | 1-3600. |
| `ErrorPollIntervalSeconds` | Integer | `10` | Wait after a dispatcher polling error. | 1-3600. |
| `LeaseDurationSeconds` | Integer | `60` | Reserve notification work for a dispatcher. | 1-3600; at least three times the HTTP timeout. |
| `HttpTimeoutSeconds` | Integer | `10` | Bound a callback HTTP attempt. | 1-300. |
| `MaximumAttempts` | Integer | `8` | Limit callback delivery attempts. | 1-100. |
| `MaximumDeliveryAgeMinutes` | Integer | `1440` | Bound the notification delivery age. | At least 1. |
| `InitialRetryDelaySeconds` | Integer | `10` | Set the starting retry delay. | 0-3600. |
| `MaximumRetryDelaySeconds` | Integer | `900` | Cap the calculated retry delay. | At least the initial delay, at most 86400. |
| `BackoffMultiplier` | Number | `2.0` | Increase retry delay between attempts. | 1.0-10.0. |
| `JitterPercent` | Integer | `20` | Vary retry timing. | 0-100. |

Startup validates these values even when dispatch is disabled. Callbacks can be retried and are not an exactly-once delivery guarantee. See [Notifications](../api/notifications.md) for the callback contract and [Notification Listener](notification-listener.md) for an optional local example.

## Security Behavior

The current channel token is `UsernameToken`. The REST implementation reads HTTP Basic credentials and checks them against channel/session authorization state. Creating a secured channel in the tutorial supplies a username and password in the JSON body; subsequent operations send the corresponding Basic header.

Basic encoding does not encrypt credentials. Use HTTPS, a trusted certificate, and private development infrastructure. Do not treat session identifiers as replacements for credentials.

Configuration discovery has separate settings. See [Configuration Discovery](../api/configuration-discovery.md) for endpoint authorization, serialized fields, and read examples:

| Key under `Isbm:ConfigurationDiscovery:` | Type | Required/default or safe example | Behavior |
| --- | --- | --- | --- |
| `SecurityLevelConformance` | Integer | Default `1` | Current startup validation accepts only 1. |
| `IsTlsEnabled` | Boolean | Default `false`; set `true` for HTTPS deployments | Reports TLS and, when true, rejects plain-HTTP ISBM requests. Does not configure an HTTPS endpoint or certificate. |
| `IsSecurityTokenEncryptionEnabled` | Boolean | Default `false` | Discovery report value; setting it does not enable a cryptographic feature. |
| `AdditionalInformationUrl` | String | Default points to the project repository | Must be an absolute HTTP/HTTPS URL. |
| `SecurityDetailsUsername` | String | Optional for the tutorial; `<DISCOVERY_USER>` | Required with a password to retrieve authenticated security details. |
| `SecurityDetailsPassword` | String | Optional for the tutorial; `<DISCOVERY_PASSWORD>` | Separate discovery credential, not a default channel password. |

Set `Isbm:ConfigurationDiscovery:IsTlsEnabled=true` for an HTTPS deployment. The Host then rejects plain-HTTP requests under `/isbm/2.0` with HTTP 400 and an `application/problem+json` response before redirects, authentication, or controller execution. This includes configuration discovery. With the default false value, the new guard does not restrict transport; existing HTTPS redirection behavior remains. The setting does not enable HTTPS, validate certificates, or detect all deployed endpoints automatically.

For the trusted HTTPS setup in this guide, set this in the Host terminal before starting or restarting the process:

```powershell
$env:Isbm__ConfigurationDiscovery__IsTlsEnabled = 'true'
```

For TLS termination through IIS or a reverse proxy, ASP.NET Core must identify the original request as HTTPS through trusted hosting/forwarding configuration before this guard runs. A raw `X-Forwarded-Proto` header alone is not trusted by the guard. The Host does not add general proxy trust automatically; an unconfigured HTTP backend hop will be rejected even if the client connected to a proxy over HTTPS. See Microsoft's [proxy and forwarded-header guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).

Rejecting a plaintext request cannot undo credentials already transmitted on that request. Clients must use a trusted HTTPS endpoint from the start. The security level remains constrained to 1 and needs a separate review against deployed behavior; enabling this flag does not establish Level 2 or production security conformance.

Channel passwords are hashed with BCrypt in the repository. Hashing is distinct from transport protection or the discovery encryption flag. Core authentication services are registered through MVC, while Basic channel checks are implemented in Host code; no standalone Basic authentication handler is established by these instructions.

Next: [Complete REST Publication Tutorial](first-message.md). Startup: [Installation](installation.md). Index: [Getting Started](README.md).
