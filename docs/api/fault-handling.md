# Fault Handling

Interpret the HTTP status, media type, and response body together. An ISBM fault name is not a substitute for status handling, and not every failure is an ISBM fault document.

**Source-reviewed draft:** this page covers shared Host faults, including configuration discovery. Outgoing listener responses follow a separate [notification status contract](notifications.md); they are not Host operation faults.

## ISBM Operation Faults

An operation fault document has a fault name:

```json
{
  "fault": "SessionFault"
}
```

| Status / fault | Meaning in the documented routes | Client action |
| --- | --- | --- |
| 400 ParameterFault | Invalid request fields, topics, content, URI, or duration. | Fix the request; do not retry unchanged input indefinitely. |
| 400 NamespaceFault | One prefix maps to conflicting names in filter metadata. | Correct the namespace collection; filters still will not be evaluated. |
| 401 SecurityTokenFault | Discovery security-details credentials missing, incorrect, or unconfigured. | Check independent discovery configuration; channel credentials do not substitute. |
| 404 ChannelFault | Channel unavailable or not authorized. | Check URI and credentials; absence cannot be distinguished safely from denied access. |
| 404 SessionFault | Session unavailable, unauthorized, malformed at the authorization gate, or closed. | Check the correct role's returned ID and credentials/lifetime. |
| 404 MessageFault | On ReadPublication, no message available. | Poll with delay/timeout only for this operation's empty-read result. |
| 404 MessageFault | On ReadRequest/ReadResponse, no eligible message available. | Poll within the exchange deadline; response reads can remain empty after request expiry. |
| 404 MessageFault | On PostResponse, no eligible request matched. | No response was created; this is not an empty-read polling case. |
| 404 SessionFault | On ReadResponse/RemoveResponse, request is unknown or belongs to another requester session. | Use the original requester's session and its own request ID. |
| 409 ChannelFault | Duplicate channel URI on create. | Inspect ownership; do not delete another application's channel. |
| 409 SecurityTokenFault | Invalid/conflicting token input or requested token removal target absent. | Correct token body; protect active credentials. |
| 409 OperationFault | AddSecurityTokens attempted on an unsecured channel. | Create a correctly secured channel; do not assume token addition can secure an open one. |
| 422 OperationFault | Wrong channel type at session open, or wrong role's session for an operation. | Use the appropriate Publication channel and provider/consumer session. |
| 500 OperationFault | Backend or unexpected operation failure. | Inspect sanitized diagnostics and assess possible partial effects before retrying. |

Not every operation can return every row. Use the tables in [Channel Management](channel-management.md), [Sessions](sessions.md), [Publication / Subscription](publication-workflow.md), and [Request / Response](request-response-workflow.md).

The registered session authorization gate runs before some parameter/content validation. A malformed sessionId can produce 404 SessionFault even though the service contains a ParameterFault branch. Do not assume the first error in a multiply-invalid request.

## HTTPS Requirement: ProblemDetails

When `Isbm:ConfigurationDiscovery:IsTlsEnabled=true`, a non-HTTPS ISBM request is rejected before redirection, authentication, and controller execution:

```json
{
  "status": 400,
  "title": "HTTPS is required for ISBM requests."
}
```

HTTP status is 400; media type is `application/problem+json`. These are the populated fields, not a captured network response or a guarantee that no other ProblemDetails fields are serialized.

There is **no ISBM fault field** in this transport rejection. A client that reads only `body.fault` loses the reason. Inspect status/media type and `title` as well.

Connect directly to trusted HTTPS and configure the endpoint correctly. Enabling the flag does not provision TLS. For TLS termination, the backend request must reflect the original HTTPS scheme through trusted hosting/proxy handling before this guard runs; raw forwarding headers are not proof of trust. See [Configuration](../getting-started/configuration.md).

Default false bypasses this particular guard, not all other middleware, and does not turn off an existing HTTPS listener. Rejecting a request cannot undo credential exposure if the client already sent it over plaintext.

## Other HTTP and Network Failures

Message-posting routes require a JSON envelope Content-Type. Rejection can be HTTP 415 without `{"fault":...}`. Framework, hosting, reverse-proxy, routing, and network/certificate failures can also produce different bodies or no HTTP response.

A plain routing 404 is not automatically MessageFault. Check the body before interpreting it as an empty queue. Success 204 has no JSON document; do not fail merely because there is nothing to deserialize.

These routes generally hide channel/session authorization failures behind 404 faults rather than an HTTP 401 challenge. This is not a promise that all Host endpoints use that rule; discovery security details have their own authentication contract.

## Retry Decisions

- Poll again after **ReadPublication, ReadRequest, or ReadResponse** returns its expected 404 MessageFault, using a bounded retry policy and request deadline where applicable. Do not poll-retry a PostResponse failure as though it were an empty read.
- Correct parameter, namespace, token, media-type, role, and transport failures before retrying.
- Investigate unavailable/unauthorized resources instead of treating their faults as empty messages.
- Treat a timeout or 500 on a state-changing operation as an uncertain outcome. Publication/request broker sends precede SQL metadata storage; retries may duplicate delivery. Repeating PostResponse after an uncertain result can create another response record.
- Do not blindly reprocess an outstanding publication. Make business processing and duplicate handling deliberate.
- Inspect HTTP errors without dumping Authorization headers, token bodies, connection strings, or sensitive payloads into shared logs.

Continue with [API Overview](overview.md) or [Compatibility](compatibility-and-limitations.md).

