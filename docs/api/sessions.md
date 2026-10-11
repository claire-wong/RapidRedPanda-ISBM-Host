# Sessions

A session identifies a client's messaging context on a channel. It is not a message, a channel identifier, or a substitute for channel credentials.

**Source-reviewed draft:** this page covers the four messaging roles and shared close operation. Detailed request-session bodies and message operations are in [Request / Response](request-response-workflow.md).

## Publication Roles

| Role | Open path after `/isbm/2.0/` | Body | Operations using its ID |
| --- | --- | --- | --- |
| Publisher / provider publication | POST `channels/{channelUri}/publication-sessions` | None | PostPublication and ExpirePublication. |
| Subscriber / consumer publication | POST `channels/{channelUri}/subscription-sessions` | Topics and optional listener/filter metadata. | ReadPublication and RemovePublication. |

Both require a Publication channel and matching credentials when the channel is secured. The subscription must exist before the publication is posted if it is to receive that publication.

Successful open returns HTTP 201:

```json
{
  "sessionId": "11111111-1111-4111-8111-111111111111"
}
```

This GUID is illustrative; always use the actual returned ID. Keep separate variables for each role. Reusing the publisher ID for a consumer read is not valid.

## Request Roles

| Role | Open path after `/isbm/2.0/` | Body | Operations using its ID |
| --- | --- | --- | --- |
| Requester / consumer request | POST `channels/{channelUri}/consumer-request-sessions` | Empty JSON object for polling; optional listenerUrl. | PostRequest, ExpireRequest, ReadResponse, RemoveResponse. |
| Responder / provider request | POST `channels/{channelUri}/provider-request-sessions` | Topics; optional listenerUrl/filterExpressions. | ReadRequest, RemoveRequest, PostResponse. |

Both require a Request channel and return 201 with sessionId. Open the responder before posting requests. Requester-opening topics are not supported; topics belong on each request.

Provider-request sessions are responders, unlike provider-publication sessions which are publishers. Keep all role IDs distinct. The [request/response reference](request-response-workflow.md) explains correlation, retained request state, and expiry; the [two-client tutorial](../getting-started/first-request-response.md) demonstrates the exchange.

## Open a Subscription

Minimal JSON body:

```json
{
  "topics": ["equipment.temperature"]
}
```

| Field | Required? | Behavior |
| --- | --- | --- |
| `topics` | Yes | Nonempty array of nonblank strings without surrounding whitespace; duplicates removed, matching case-sensitive. |
| `listenerUrl` | No | Optional availability callback address. Omit for polling. A supplied URL must be absolute HTTP/HTTPS, have a host, no user information/fragment, no surrounding whitespace, and fit the 2048-character normalized limit. |
| `filterExpressions` | No | Optional structurally validated metadata, persisted but not evaluated for delivery. |

Subscription topic rules generate a broker filter. Excessive topic count/length can exceed the generated 1024-character filter limit and return ParameterFault; the publication's 32-topic limit is not a blanket subscription-topic limit.

The Host creates a broker subscription for this session. Losing that subscription/topic is not repaired by replaying old publications; do not assume recovery of preexisting broker messages.

For callback paths, role-specific bodies, and delivery rules, see [Notifications](notifications.md). The optional [local listener walkthrough](../getting-started/notification-listener.md) extends the publication tutorial.

### Filter Metadata

An expression entry contains `expressionString` with `expression` and `language`, plus optional `languageVersion`, `applicableMediaTypes`, and `namespaces`. Namespace entries use `prefix` and `name`.

The language must be nonblank and expression must be present; an empty expression string is accepted, while a nonempty whitespace-only expression is rejected. Supplied media-type entries and namespace prefix/name values must be nonblank. Mapping one prefix to different namespace names returns NamespaceFault.

These checks validate structure, **not expression execution or language support**. Content filtering must be performed by the receiving application. Topic selection remains active even when filter metadata is supplied.

## Credentials and Lifetime

Use the same normalized channel identity that opened a secured session for its subsequent protected calls. Another valid token for the same channel is not automatically the session's identity. Credential removal and channel deletion can affect existing access.

A successful open does not establish an automatic lease duration, exactly-once delivery, lock renewal, or multi-instance recovery guarantee. Close sessions explicitly; do not assume that disconnecting a client closes its Host/broker context.

## Close Any Messaging Session

```text
DELETE /isbm/2.0/sessions/{sessionId}
```

No request body. Supply the appropriate Basic credentials. Successful close returns HTTP 204 with no document.

This shared route closes publication, subscription, provider-request, and consumer-request sessions. For a publication subscription it deletes the Host-managed broker subscription and updates Host lifecycle state. For a provider publication it closes that publisher's context; it does not delete the channel.

Close only sessions created by your client. Closing a consumer with unprocessed messages can discard its broker backlog. Finish processing/removal before closing, then delete only disposable demonstration channels.

Missing/already closed sessions return `404 SessionFault`; do not expect repeated closes to always return 204.

## Errors

| Operation / condition | Source-derived outcome |
| --- | --- |
| Open with invalid URI, session-opening body/topics/listener/filter structure | 400 ParameterFault |
| Conflicting filter namespace mappings | 400 NamespaceFault |
| Open missing/unauthorized channel | 404 ChannelFault |
| Open a publication role on a Request channel, or a request role on a Publication channel | 422 OperationFault |
| Use a session of another kind for a messaging operation | 422 OperationFault |
| Operate on unavailable/unauthorized/closed session | 404 SessionFault |
| Open/close fails in backend or unexpectedly | 500 OperationFault |

In the registered authorization pipeline, malformed session IDs can be rejected as 404 SessionFault before service-level parameter validation. Do not promise 400 for every malformed session route.

Continue with [Publication / Subscription](publication-workflow.md), [Request / Response](request-response-workflow.md), or [Fault Handling](fault-handling.md). Two-window examples are in [Your First Message](../getting-started/first-message.md) and [Your First Request and Response](../getting-started/first-request-response.md).

