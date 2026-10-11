# Security and Deployment

The beginner guide covers Windows development, not a hardened production deployment. This page identifies responsibilities and boundaries; it does not certify security conformance.

## Client Endpoint

Use trusted HTTPS from the initial request. Basic encoding is not encryption. Create secured channels with tokens from the start: AddSecurityTokens cannot secure a channel with no active tokens. Removing the last active token leaves a channel unsecured.

Channel creation has no existing channel credential to check. Independently control API access through hosting/network controls. Session IDs are not authorization substitutes.

Set `Isbm:ConfigurationDiscovery:IsTlsEnabled=true` for HTTPS deployment. It reports the flag and rejects plain-HTTP ISBM requests with HTTP 400 ProblemDetails. The false default is not a recommendation to send secrets over HTTP. This setting does not provision certificates, configure TLS, secure callbacks, or establish higher conformance.

See [Configuration](../getting-started/configuration.md#security-behavior) and [Channel Management](../api/channel-management.md).

## Hosting and Proxies

Provide protected runtime identities/configuration, valid certificates, process supervision, and Production environment settings. Development certificates, user secrets, and Swagger are not a production security plan.

For TLS termination, trusted forwarding/hosting configuration must establish the original HTTPS scheme before the guard. General forwarded-header trust is not configured automatically. An arbitrary X-Forwarded-Proto header is insufficient. Validate the actual path instead of disabling the guard to mask rejection.

Linux, IIS/reverse proxies, and published production deployment have not been exercised by these guides. Docker is deferred. See [Installation](../getting-started/installation.md#publish-an-application-folder).

## Backends and Secrets

Separate installer from runtime permissions. Protect SQL/namespace credentials and their configuration sources. The Host continues to need broker administrative permissions at runtime; check entity-management workflows before changing rights.

Do not store secrets in tracked settings, examples, or issue reports. Environment variables and development user secrets are not encrypted vaults. Discovery security-details credentials are independent of channel credentials. The discovery encryption flag is not a cryptographic implementation switch.

## Callback Trust

Listener URLs cause outbound Host HTTP requests. Session-creation access, permitted destinations, and egress restrictions are part of the trust boundary. URL structural validation is not a network allowlist or proof that internal destinations are inaccessible.

The callback client does not supply channel Basic credentials or application signatures. Use a trusted listener/network design compatible with [Notifications](../api/notifications.md). HTTP/HTTPS URLs are structurally accepted; incoming TLS enforcement does not alter this.

## Before Wider Deployment

Validate fresh installation, both messaging patterns, callbacks, TLS rejection, authorization failures, and recovery scenarios in an authorized environment. Reconcile SQL/broker state after failures; do not assume atomic cross-system transactions or exactly-once processing.

The current documentation branch has a request-expiry notification ownership limitation. A separate fix branch does not establish that deployed software includes it. Review [Compatibility and Limitations](../api/compatibility-and-limitations.md#known-request-expiry-limitation) before permitting untrusted applications to share a deployment.

Return to the [documentation index](../README.md).
