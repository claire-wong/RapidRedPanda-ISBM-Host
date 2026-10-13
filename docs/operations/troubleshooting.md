# Troubleshooting

Record the operation, HTTP status, content type, software revision, UTC time, and sanitized Host logs. Note whether SQL or broker state might have changed. Never share credentials, Basic headers, connection strings, secret-bearing configuration, or private message payloads.

## Startup and Installation

| Symptom | Check |
| --- | --- |
| Startup validation fails | Compare effective [Configuration](../getting-started/configuration.md). Worker option constraints also apply when disabled. |
| Missing SQL object | Verify the database and SchemaVersions using [Installation](../getting-started/installation.md#verify-the-schema). An old marker can skip later installation. |
| SQL connection fails | Check identity, database, network, permissions, and certificate trust; do not bypass trust checks. |
| Channel/session returns 500 | Inspect backend logs and namespace permissions/tier/connectivity. Do not assume no partial effects. |
| HTTPS fails | Verify hostname, certificate validity/trust, and actual listening URL before sending credentials. |
| Swagger unavailable | Swagger is Development-only. Use documented REST rather than enabling Development on a deployed service to obtain Swagger. |

## HTTP and Authentication

HTTP 400 `application/problem+json` can come from the HTTPS guard before authentication/controllers, not an ISBM ParameterFault. Use HTTPS from the start and trusted proxy scheme handling before the guard; see [Security and Deployment](security-and-deployment.md).

Discovery security-details and channel/session operations use different credentials. Missing/inaccessible resources can produce ChannelFault or SessionFault rather than reveal existence. Consult [Fault Handling](../api/fault-handling.md).

For a routing 404, check the path, identifier encoding, and body. A generic 404 does not mean an empty queue. HTTP 204 has no JSON response body.

## Exchanges

- Open receiving sessions before posting; match topics case-sensitively.
- Use the correct channel type and session role. IDs do not replace authorization.
- Serialize read/process/remove within a session; outstanding messages can be returned again.
- Bound polling by the application's deadline. MessageFault can mean no eligible message.
- Correlate responses with the original request ID, not the new response ID.
- Avoid blind reposts: repeated posts can create separate messages/responses.
- Apply required content filtering in clients; accepted expressions are not evaluated by the Host.

See [Complete REST Publication Tutorial](../getting-started/first-message.md), [Complete REST Request/Response Tutorial](../getting-started/first-request-response.md), and [Compatibility and Limitations](../api/compatibility-and-limitations.md).

## Callbacks

A listener must be reachable from the Host. A callback loopback URL points to the Host machine, not necessarily the client machine. Check the exact path, method, body, and **204** acknowledgement in [Notifications](../api/notifications.md).

Timeouts, other statuses, and network failures can cause retries. Listeners must tolerate repeated availability signals. Finite retry limits/deadlines mean a notice may stop being retried; application polling/reconciliation may remain necessary.

Check dispatcher enablement, sanitized logs, endpoint trust, and lifecycle/expiry conditions. Incoming TLS enforcement does not secure callbacks. Do not disable certificate validation to mask trust failures.

## Escalating an Issue

Provide a redacted operation sequence, revision, expected/observed behavior, statuses/faults, and sanitized logs. State whether an existing database, proxy, retry, callback, or expiry was involved. Reproduce only in an authorized isolated environment; never reset shared state as a troubleshooting shortcut.

Return to [Maintenance and Recovery](maintenance.md) or the [documentation index](../README.md).
