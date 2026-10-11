# Configuration Discovery

Discovery reports declared Host capabilities and configured security settings. It is not a SQL Server/Azure health check or a standards-conformance certificate.

**Source-reviewed draft:** fields and authorization rules below come from the current controller and contracts, not captured live responses.

## Endpoints

| Method and path | Credentials | Success |
| --- | --- | --- |
| GET `/isbm/2.0/configuration/supported-operations` | No discovery credentials required. | 200 JSON |
| GET `/isbm/2.0/configuration/security-details` | Independent discovery HTTP Basic credentials. | 200 JSON |

The HTTPS guard applies to both routes when enabled. Missing, incorrect, or unconfigured discovery credentials produce **401 SecurityTokenFault** on security-details, not the channel/session routes' typical 404 fault. Do not rely on a WWW-Authenticate challenge.

## Supported Operations

The default declaration has this shape:

```json
{
  "isXMLFilteringEnabled": false,
  "isJSONFilteringEnabled": false,
  "supportedContentFilteringLanguages": {
    "contentFilteringLanguages": []
  },
  "supportedAuthentications": {
    "soapSupportedTokenSchemas": [],
    "restSupportedAuthenticationSchemes": [
      {
        "schemeName": "Basic",
        "schemeInfoUrl": "https://www.rfc-editor.org/rfc/rfc7617"
      }
    ]
  },
  "securityLevelConformance": 1,
  "isDeadLetteringEnabled": true,
  "isChannelCreationEnabled": true,
  "isOpenChannelSecuringEnabled": false,
  "isWhitelistRequired": false,
  "defaultExpiryDuration": null,
  "additionalInformationURL": "https://github.com/claire-wong/RapidRedPanda-ISBM-Host"
}
```

`additionalInformationURL` is configurable. Security-level validation currently permits level 1 only. Other declarations shown are fixed by the controller.

This document is a capability summary, not an enumeration of REST routes. Basic does not itself encrypt credentials. Empty SOAP schemas do not imply a supported SOAP interface.

False filtering declarations agree with [accepted-but-not-applied filters](compatibility-and-limitations.md). Open-channel securing is false: adding tokens cannot convert an unsecured channel into a secured one. A null default expiry does not mean permanent retention.

Dead-lettering true is a declaration, not proof of backend health, an exposed dead-letter retrieval API, or automatic recovery. Whitelist false does not establish unrestricted production access or an outbound callback allowlist.

## Security Details

Default configured reporting:

```json
{
  "isTLSEnabled": false,
  "isSecurityTokenRequired": false,
  "isSecurityTokenEncryptionEnabled": false,
  "isCertificateRequired": false,
  "isRBACEnabled": false,
  "isKeyManagementServiceEnabled": false,
  "isEndToEndMessageEncryptionEnabled": false
}
```

`isTLSEnabled` and `isSecurityTokenEncryptionEnabled` reflect configuration. The other fields are controller declarations.

`isSecurityTokenRequired=false` does not bypass credentials on secured channels or the discovery endpoint itself. `isCertificateRequired=false` does not authorize bypassing server-certificate trust.

Setting the token-encryption reporting option does not implement encryption. The TLS option differs: when true it also rejects non-HTTPS requests under `/isbm/2.0` with **400 ProblemDetails**, before redirects or controllers. It does not create a certificate or HTTPS listener, detect TLS automatically, secure outgoing notifications, or raise security conformance above level 1.

False is the default for that guard; it does not turn off an existing HTTPS listener. For deployment and trusted proxy handling, see [Configuration](../getting-started/configuration.md) and [Fault Handling](fault-handling.md).

## Read the Declarations

In PowerShell 7, use the actual trusted HTTPS Host address:

```powershell
$discoveryBase = 'https://localhost:7443'
if ([uri]::new($discoveryBase).Scheme -ne 'https') {
    throw 'Use the trusted HTTPS Host address.'
}
$supported = Invoke-RestMethod -Method Get -Uri "$discoveryBase/isbm/2.0/configuration/supported-operations" -Headers @{ Accept = 'application/json' } -MaximumRedirection 0
$supported
```

Before the next block, the operator must configure independent discovery credentials as described in [Configuration](../getting-started/configuration.md). There is no demonstration password to assume. These are not channel, SQL, or Azure credentials.

```powershell
$discoveryCredential = Get-Credential -Message 'Enter the configured discovery credentials'
if ($null -eq $discoveryCredential -or
    [string]::IsNullOrWhiteSpace($discoveryCredential.UserName) -or
    $discoveryCredential.UserName.Contains(':') -or
    [string]::IsNullOrEmpty($discoveryCredential.GetNetworkCredential().Password)) {
    throw 'Use a nonempty username without a colon and a nonempty password.'
}
$pair = '{0}:{1}' -f $discoveryCredential.UserName, $discoveryCredential.GetNetworkCredential().Password
$discoveryHeaders = @{
    Accept = 'application/json'
    Authorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($pair))
}
Remove-Variable pair
try {
    $security = Invoke-RestMethod -Method Get -Uri "$discoveryBase/isbm/2.0/configuration/security-details" -Headers $discoveryHeaders -MaximumRedirection 0
    $security
} finally {
    Remove-Variable discoveryCredential, discoveryHeaders
}
```

Do not print headers or capture credentials in shared transcripts. A 401 requires checking discovery configuration and credentials, not creating a channel token. A successful declaration does not prove the first-message workflow works.

Return to [API Overview](overview.md).

