# Availability Notifications

Notifications are optional Host-to-client callbacks. They announce availability; they do not contain the application payload or acknowledge business processing.

**Source-reviewed draft:** contracts and retry rules were inspected in source. Callback delivery and the listener walkthrough have not been exercised against a live Host.

## Register a Listener

Supply `listenerUrl` when opening the receiving session. See [Sessions](sessions.md).

| Notice | Receiving session | Callback body |
| --- | --- | --- |
| Publication | Consumer subscription | `{"topics":["equipment.temperature"]}` |
| Request | Provider request (responder) | `{"topics":["equipment.status"]}` |
| Response | Consumer request (requester) | `{"requestMessageId":"original-request-guid"}` |

Provider publication sessions do not receive availability notices. Omit the listener URL to use normal polling.

The URL must be absolute HTTP or HTTPS, have a host, and contain no user information or fragment. Whitespace and excessive URL length are rejected. Use a reachable listener base URL, not the complete notification path. A loopback address refers to the Host's machine, not a remote client's machine.

## Callback Contract

The Host sends **PUT** to an external listener, not to an ISBM Host route:

```text
{listenerBasePath}/notifications/{recipientSessionId}/{messageId}
```

For example, a listener base of `https://listener.example/listener/` receives requests under `https://listener.example/listener/notifications/...`. The Host removes the trailing base-path slash before appending the suffix; any existing query is preserved.

Both identifiers are GUIDs. The path's message ID identifies the publication, request, or **response** being announced. For a response notice, the original request ID is instead in the JSON body.

Publication/request body:

```json
{"topics":["equipment.temperature"]}
```

Response body (illustrative original request ID):

```json
{"requestMessageId":"11111111-1111-4111-8111-111111111111"}
```

Bodies use JSON; the client sets User-Agent to `RapidRedPanda.ISBM.Host`. There is no notification-record ID or application message content in the body.

Use the receiving session and normal [publication](publication-workflow.md) or [request/response](request-response-workflow.md) read operation to retrieve content. Response reads use the original request ID, not the callback's response ID. A callback can race with reads, removal, closure, or expiry: the Host read result is authoritative.

## Listener Response and Retries

| Result | Host treatment |
| --- | --- |
| HTTP 204 | Delivered; no response body is needed. |
| HTTP 408, 429, or 500-599 | Retryable failure, subject to eligibility, delivery deadline, and attempt budget. |
| Any other HTTP status | Terminal failure, including 200, 201, 202, redirects, and authentication failures. |
| HTTP request exception or request timeout | Retryable failure. |

Redirects are disabled. The listener's response body is not used. Do not return 200 merely because your framework uses it by default.

The shared dispatcher uses the `PublicationNotifications` configuration section for **all three** notice types. See the option table in [Configuration](../getting-started/configuration.md).

Defaults include eight attempts as the retry-failure budget, a 10-second HTTP timeout, and a maximum delivery age of 1,440 minutes. Backoff starts at 10 seconds, doubles, and caps at 900 seconds, with additive jitter of up to 20 percent before the cap. Actual scheduling also depends on polling, leases, database work, and concurrency. Retry-After is not interpreted by this client.

The attempt threshold is applied when completing a retryable failure. It is not an exact guarantee of eight HTTP calls under lease loss or recovery. A retry that would reach or exceed the persisted delivery deadline is not scheduled.

Publication notices also depend on publication lifetime. Request notice deadlines are bounded by request expiry when an effective expiry exists. Response notices use their own delivery window; original request expiry is not checked by response-notification eligibility. A response notice therefore does not prove a new response can still be claimed.

Disabling the dispatcher stops callback sends, not necessarily notification creation. Do not assume an unlimited backlog or guaranteed replay when it is reenabled.

## Lifecycle and Duplicate Handling

Publication eligibility requires an open receiving session, a listener, an unexpired publication, and a pending recipient. Reading changes that recipient to outstanding and can suppress later notices.

Request eligibility checks the open responder session, listener, request existence and expiry; it does not apply the publication recipient-pending rule. Response eligibility checks an open requester session and an existing, unremoved response, not original request expiry.

Eligibility is rechecked before sending, but races remain. Session closure and expired delivery windows can prevent further attempts. Successful HTTP delivery followed by a failed state update or an expired lease can cause duplicates.

No exactly-once delivery, callback ordering, or guaranteed business consumption is promised. Use session/message identifiers for deliberate deduplication; return 204 for a safely accepted duplicate. In production, acknowledge after durable acceptance and process asynchronously. Keep normal read/removal and recovery behavior separate from callback acknowledgment.

## Security Boundary

The callback client does not add channel Basic credentials or a signature. User-Agent and identifiers are not authentication. Cookies and redirects are disabled.

Incoming ISBM HTTPS enforcement does not require outgoing callbacks to use HTTPS. Configure a trusted HTTPS listener and suitable network access controls for nonlocal use; certificate trust, endpoint authentication, and outbound destination restrictions require deployment review. Do not assume the discovery whitelist flag provides an outbound allowlist.

The [local listener walkthrough](../getting-started/notification-listener.md) deliberately uses loopback HTTP for observation only. It is not a production listener implementation.

Continue with [Configuration Discovery](configuration-discovery.md) and [Compatibility](compatibility-and-limitations.md).

