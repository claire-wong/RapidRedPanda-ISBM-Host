# Direct REST API

Use this approach when an application or tool can issue HTTPS requests directly. No optional ClientAdapter package or CLI Wrapper is required.

The Host exposes ISBM 2.0 REST services under `/isbm/2.0`. This minimal example calls configuration discovery to confirm that the REST endpoint is reachable.

## Minimal Example

```powershell
$baseUrl = 'https://localhost:7443/isbm/2.0'
$response = Invoke-RestMethod `
    -Method Get `
    -Uri "$baseUrl/configuration/supported-operations" `
    -Headers @{ Accept = 'application/json' } `
    -SkipHttpErrorCheck `
    -StatusCodeVariable status
"HTTP $status"
$response
```

## What This Checks

- The Host endpoint is reachable at the configured URL.
- The discovery route responds.
- The client can send and receive JSON over HTTP.

## What This Does Not Check

- It does not create channels or sessions.
- It does not publish, read, remove, or expire messages.
- It does not validate end-to-end SQL Server or Azure Service Bus messaging.
- It does not prove a deployment is production-ready.

## Next Steps

- [Your First Message](../getting-started/first-message.md)
- [Your First Request and Response](../getting-started/first-request-response.md)
- [REST API overview](../api/overview.md)
- [Publication / Subscription](../api/publication-workflow.md)
- [Request / Response](../api/request-response-workflow.md)

Use HTTPS from the start for credential-bearing calls. Return to [Integration Options](README.md).