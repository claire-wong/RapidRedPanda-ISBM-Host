# ISBM REST API

RapidRedPanda exposes OpenO&M ISBM 2.0 messaging services under `/isbm/2.0`. This reference describes the current Host implementation, not every interface in the ISBM specification.

**Source-reviewed draft:** examples and status codes are implementation-derived expectations, not captured live responses. SQL Server, Azure Service Bus, and end-to-end deployment still require validation.

## Choose Your Path

New to messaging? Begin with [Core Concepts](../introduction/core-concepts.md), then [Getting Started](../getting-started/README.md) and [Complete REST Publication Tutorial](../getting-started/first-message.md).

| Reference | Use it to |
| --- | --- |
| [Channel Management](channel-management.md) | Create and identify channels; manage channel credentials. |
| [Sessions](sessions.md) | Separate publisher and consumer contexts; close them safely. |
| [Publication / Subscription](publication-workflow.md) | Post, read, remove, and explicitly expire publications. |
| [Request / Response](request-response-workflow.md) | Correlate requester/responder exchanges and manage their lifecycle. |
| [Notifications](notifications.md) | Register listeners and handle availability callbacks, retries, and duplicates. |
| [Configuration Discovery](configuration-discovery.md) | Read declared capabilities and independently authenticated security details. |
| [Message Content](message-content.md) | Choose native JSON, typed text, or Base64-encoded bytes. |
| [Fault Handling](fault-handling.md) | Interpret HTTP status and response shape together. |
| [Compatibility and Limitations](compatibility-and-limitations.md) | Understand implemented, deferred, and unvalidated behavior. |

This reference covers shared contracts, publication/subscription, request/response, notifications, and configuration discovery. Try the optional [local listener walkthrough](../getting-started/notification-listener.md) after the publication tutorial.

After the publication tutorial, try [Complete REST Request/Response Tutorial](../getting-started/first-request-response.md) with two client windows.

## Addressing and Headers

For a locally configured HTTPS endpoint, a channel-list URL could be:

```text
https://localhost:7443/isbm/2.0/channels
```

The port is an example, not a Host default. Use the actual address from [Installation](../getting-started/installation.md).

Use `Accept: application/json`. Send JSON bodies with `Content-Type: application/json; charset=utf-8`, including token-removal DELETE requests. Message-posting operations also accept an `application/*+json` envelope media type. Payload metadata does not change the HTTP envelope's media type.

Protected operations use:

```text
Authorization: Basic <base64-of-UTF8-username:password>
```

This is a schematic header, not a credential. Base64 is not encryption. Connect to a trusted HTTPS endpoint from the outset; never send credentials to HTTP expecting a redirect to protect them. Do not log headers or credential-bearing bodies. The [Complete REST Publication Tutorial](../getting-started/first-message.md) prompts for credentials without publishing a usable password.

Channel credentials are different from SQL credentials, Azure connection settings, and discovery security-details credentials. Channel creation supplies tokens in its JSON body; a Basic header alone does not create a token. Channel creation is not gated by an existing channel credential, and unsecured channels are accessible without channel credentials. Do not assume a global administrator/login boundary around these routes; restrict endpoint access appropriately.

## Identifiers

| Identifier | Meaning | Example / rule |
| --- | --- | --- |
| Channel URI | Application-facing name returned as `uri` | `urn:rapidredpanda:example:temperature` |
| Channel route value | URI encoded as one URL path segment | `urn%3Arapidredpanda%3Aexample%3Atemperature` |
| Session ID | Host-generated GUID returned as `sessionId` | Retain the ID for the correct client role. |
| Message ID | Host-generated GUID returned as `messageId` | Not interchangeable with a session ID. |

For PowerShell URL construction:

```powershell
$channelUri = 'urn:rapidredpanda:example:temperature'
$channelRoute = [uri]::EscapeDataString($channelUri)
```

Encode the channel identifier, not the entire URL, and do not manually replace URI punctuation. Route tables use `{channelUri}` for this encoded segment even though controller parameter names use `channelId`. This is not the internal database GUID or broker topic name.

URN examples avoid slash-bearing path identifiers. Encoded slashes and proxy/IIS path handling have not been validated; test your chosen identifier through the actual deployment before relying on it.

## Authorization and Transport

Use the same channel identity that opened a secured session when operating on that session. Credentials are rechecked; a session ID is not an authentication credential. Username matching normalizes trimmed usernames using invariant uppercase; passwords are case-sensitive. Use a username without a colon because Basic parsing splits at the first colon.

Unavailable or unauthorized channels/sessions commonly return `404 ChannelFault` or `404 SessionFault`, not a Basic-authentication `401` challenge. A wrong-role session can return `422 OperationFault`. See [Fault Handling](fault-handling.md); validation/authorization order affects which error is returned.

`Isbm:ConfigurationDiscovery:IsTlsEnabled` defaults to false. When true, the Host both reports TLS enabled and rejects non-HTTPS requests under `/isbm/2.0` with HTTP 400 ProblemDetails before redirection or operation execution. This setting does not create an HTTPS listener, certificate, or trusted proxy configuration. See [Configuration](../getting-started/configuration.md).

## Response Conventions

Successful create/post/open operations normally return 201 with the resource description or generated ID. Reads return 200; successful close/remove/delete operations generally return 204 with no JSON body. Adding channel tokens returns 201 with no response document.

Do not assume a Location header, response body on every success, or one error envelope for every HTTP failure. The operation pages specify exact source-derived contracts. See [Fault Handling](fault-handling.md) before implementing polling or retries.

