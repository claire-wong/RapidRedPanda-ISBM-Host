# Compatibility and Limitations

This page separates current implementation scope from deferred capabilities and unvalidated deployment claims. It is not a certification of full ISBM 2.0 compliance or production readiness.

**Source-reviewed draft:** shared contracts and publication behavior have been inspected in source. Live installation, SQL/Azure messaging, and proxy deployment were not exercised for this documentation batch.

## Current Release Scope

| Area | Current boundary |
| --- | --- |
| Purpose | OpenO&M ISBM 2.0 messaging; ISA-95 Part 6 is the standards context, not a promise to implement all ISA-95 information/activity models. |
| Interface | ISBM REST routes. SOAP is not provided as a supported first-release interface. |
| Runtime/backend | .NET 10, SQL Server, Azure Service Bus. No alternative database/broker support is claimed. |
| Messaging | Channel/session management, publication/subscription, request/response, availability notifications, and configuration discovery exist in the Host. |
| Reference coverage | Shared contracts, publication/subscription, request/response, notifications, and discovery are documented as source-reviewed drafts. |
| Tokens | UsernameToken only; flat and nested username/password forms parsed. Omitted flat type defaults to UsernameToken, but omitted tokens do not generate credentials. |
| Payloads | Native JSON object, typed text, and Base64 bytes in a JSON messageContent envelope. |
| Business models | CCOM or other models are optional application agreements. No CCOM schema-validation capability is implied. |

See [API Overview](overview.md) for navigation and [Getting Started](../getting-started/README.md) for setup.

## Accepted but Not Applied

Structurally valid `filterExpressions` are accepted/persisted for compatibility, but the server does **not evaluate them to filter delivery**. Invalid structure can still fail, including namespace conflicts.

Topic selection remains active: publication and subscription topics match case-sensitively using shared values. Applications must perform required content filtering themselves. Do not interpret accepted filter metadata as proof that a language or expression is executable. See [Sessions](sessions.md).

This is a verified deferred behavior, not a claim that it is the only missing feature in the entire specification.

## Credentials and Security Boundaries

- Channel access uses active UsernameToken credentials through HTTP Basic. Base64 is not encryption; use trusted HTTPS.
- Create secured channels with tokens from the beginning. AddSecurityTokens cannot secure a channel that has no active tokens.
- Removing the last token leaves the channel unsecured. Token addition is not a password-update operation. See [Channel Management](channel-management.md).
- Session IDs do not replace credentials. Unauthorized/missing resources can be hidden behind ChannelFault/SessionFault.
- Channel creation is not protected by an existing channel token. Endpoint/network access controls are separate deployment responsibilities.
- TLS discovery/enforcement is explicit configuration, not automatic endpoint/certificate detection. The false default does not enforce HTTPS through the new guard.
- Enabling TLS enforcement does not establish a higher conformance level. The current discovery validation still accepts security-level conformance 1.
- The incoming HTTPS guard does not enforce notification callback transport. Callback URL syntax permits HTTP and HTTPS; deployment trust/security require separate consideration.

See [Configuration](../getting-started/configuration.md) and [Fault Handling](fault-handling.md).

## Delivery and Lifecycle Limits

Subscriptions must be opened before publications are sent. Reads and removals use consumer sessions; posts and expiry use provider sessions. Removal affects one consumer's outstanding delivery, not every subscriber.

Publication posting supports at most 32 distinct topics. Subscription topic sets are constrained by the generated broker-filter size, not a universal 32-topic subscription rule. Native payload content must be an object; unsupported shapes/encodings are rejected.

Positive expiry durations set explicit lifetime. Unset or nonpositive duration does not mean immediate expiry or permanent retention. Already-outstanding publication content can remain readable after explicit expiry; expiry cannot undo client processing.

A failed publish can have partial effects because broker send precedes SQL metadata recording. Do not assume atomic SQL/broker transactions, safe blind retries, exactly-once business processing, or unrestricted delivery/recovery guarantees. Validate representative message sizes and required processing/recovery scenarios in your deployment.

Optional [notifications](notifications.md) signal availability, not the business payload or successful processing. Only HTTP 204 acknowledges delivery; duplicates, races, finite retries, and suppressed/expired notices are possible. Callback credentials/signatures are not supplied by the Host client.

[Configuration discovery](configuration-discovery.md) reports declarations and configured flags, not backend health or certification. Its security-details credentials are independent of channel credentials.

Request/response can fan out to multiple matching responders and can produce multiple responses per request. The response URL uses the original request ID, while each created response has a new message ID. Repeated posts are not an idempotency mechanism.

RemoveRequest acknowledges one responder's delivery, not deletion of the correlation record. Active requests can still receive responses after removal. An expired request that remains outstanding for a responder can also accept a response, but the requester cannot claim a new response after expiry. Already-outstanding responses remain readable until removal. See [Request / Response](request-response-workflow.md) for these boundaries.

## Known Request Expiry Limitation

In the current documentation branch, ExpireRequest scopes the request update to the requester session/channel, but attempts notification suppression even when no owned request was updated. A supplied foreign request ID can therefore affect pending request-availability notices. A 204 does not prove an owned transition or harmless no-op.

Expire only IDs obtained for the caller's own requests. This client precaution is not a security boundary against untrusted callers. Integrate and separately SQL-validate the ownership and already-expired no-op fix before removing this limitation from release guidance. Documentation alone does not fix it.

## Deployment and Release Readiness

Docker deployment is **deferred to a future release**. This first-release documentation does not offer a supported Docker workflow.

The getting-started path is Windows-oriented. Linux and IIS/reverse-proxy deployment have not been validated by this guide. Trusted certificate setup and original HTTPS scheme handling are not automatically supplied by the TLS option.

The SQL initializer is documented for fresh installation, not as a proven upgrade/migration path. Host-managed topic recreation is not a promise to recover deleted messages or prior subscriptions.

Installation and message examples remain source-reviewed until exercised against an authorized development environment. Static checks and prior focused unit tests do not establish live SQL/Azure readiness or full standards conformance.

For practical guidance, use [Installation](../getting-started/installation.md), [Security and Deployment](../operations/security-and-deployment.md), [Maintenance and Recovery](../operations/maintenance.md), and [Troubleshooting](../operations/troubleshooting.md).

