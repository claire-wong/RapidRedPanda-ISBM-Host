# Integration Options

Applications communicate with RapidRedPanda ISBM Host through the OpenO&M ISBM 2.0 REST interfaces. You can call those REST interfaces directly, or use optional client-side tools that wrap the same contracts.

![RapidRedPanda integration options showing .NET ClientAdapter, CLI Wrapper, and direct REST approaches](../images/RapidRedPanda-Integration-Options.png)

The diagram is conceptual. All three approaches ultimately use the Host's ISBM REST interfaces. The .NET ClientAdapter and CLI Wrapper are optional external components with their own versions, licenses, and documentation; they are not included in the Host distribution. Python applications can invoke the CLI with `subprocess`, but they can also call the REST API directly. Published packages and documentation do not, by themselves, establish completed compatibility testing against the current Host staging release.

The helper options reduce client-side plumbing, not the ISBM lifecycle itself. Applications still choose channels and topics, open sessions, retain session and message identifiers, decide when publications may expire, and verify subscriber delivery separately.

## Shared Example Scenario

The examples use the same publisher-side scenario: open a provider publication session, post one `equipment.temperature` message, and display the returned session and message identifiers.

| Setting | Value |
| --- | --- |
| Host | `https://localhost:7443/isbm/2.0` |
| Publication channel | `urn:rapidredpanda:demo:publication` |
| Topic | `equipment.temperature` |
| Payload | `{"equipmentId":"PUMP-101","temperature":72.5,"status":"Normal"}` |

A consumer subscription must already be open before publication if you want to verify retrieval with the current Host implementation. Closing a provider publication session expires remaining unexpired publications from that session, so close only when that expiration is acceptable. The ClientAdapter and CLI Wrapper expose convenience operations; they do not remove these lifecycle responsibilities.

## Choose an Approach

| Approach | Best fit | Basic example |
| --- | --- | --- |
| .NET ClientAdapter | C# and .NET applications that want service classes instead of hand-written HTTP calls. | [Publish with the .NET ClientAdapter](dotnet-clientadapter.md) |
| CLI Wrapper | Automation, shell scripts, Python subprocess integration, and command-driven applications that want JSON command output. | [Publish with the CLI Wrapper](cli-wrapper.md) |
| Direct REST API | Any language or tool that can issue HTTPS requests. | [Call the REST discovery endpoint](direct-rest.md) |

## Responsibility Comparison

| Responsibility | Direct REST | .NET ClientAdapter | CLI Wrapper |
| --- | --- | --- | --- |
| HTTP request construction | Application builds methods, URLs, headers, and bodies. | Library service methods handle HTTP mechanics. | CLI commands handle HTTP mechanics through the Wrapper and ClientAdapter. |
| ISBM message envelope | Application constructs the JSON envelope. | Adapter formats supported publication envelopes from payload and topic inputs. | Wrapper formats supported envelopes from command arguments. |
| Authentication | Application builds the Basic authorization header. | Application configures service authentication and credentials. | Application passes credentials to each command. |
| Response handling | Application parses status codes and fault bodies. | Application checks typed response fields. | Application checks exit codes and JSON results such as `success`, `fault`, and `transportFault`. |
| Session management | Application opens, stores, uses, and closes sessions. | Application still opens, stores, uses, and closes sessions. | Application still opens, parses, passes, and closes session IDs. |
| Message expiration | Application supplies expiry, expire, and close decisions. | Application supplies expiry, expire, and close decisions. | Application supplies expiry, expire, and close decisions. |
| Error handling | Application owns retry and duplicate-risk policy. | Application owns retry and duplicate-risk policy with typed diagnostics. | Application owns retry and duplicate-risk policy with JSON diagnostics. |
| Best fit | Any HTTP-capable language; maximum transparency. | C# and .NET applications. | Shell, Python `subprocess`, automation, and agents. |

## Direct REST Comparison

A direct REST client works at the protocol level. This illustrative request posts the shared payload after the client has already opened a provider publication session and retained `{providerSessionId}`:

```http
POST /isbm/2.0/sessions/{providerSessionId}/publications HTTP/1.1
Host: localhost:7443
Authorization: Basic <base64-channel-credentials>
Content-Type: application/json
Accept: application/json

{
  "topics": ["equipment.temperature"],
  "messageContent": {
    "content": {
      "equipmentId": "PUMP-101",
      "temperature": 72.5,
      "status": "Normal"
    }
  }
}
```

This is not a complete workflow. A direct REST client must open the provider publication session, build the Basic authorization header, serialize the ISBM JSON envelope, check the HTTP status and fault body, retain returned identifiers, and decide when to close the provider session. For a complete executable PowerShell publication/subscription workflow, use [Your First Message](../getting-started/first-message.md).

## Authoritative References

- [.NET ClientAdapter Coding Guide](https://github.com/claire-wong/RapidRedPanda.ISBM.ClientAdapter.SDK.Samples/blob/main/Documents/RapidRedPanda-ISBM-Coding-Guide.md)
- [RapidRedPanda ISBM Wrapper](https://github.com/claire-wong/RapidRedPanda-ISBM-Wrapper)
- [Wrapper Python samples](https://github.com/claire-wong/RapidRedPanda-ISBM-Wrapper/tree/main/samples/python)
- [Host REST API overview](../api/overview.md)

For a complete direct REST walkthrough, start with [Your First Message](../getting-started/first-message.md). Return to the [documentation index](../README.md).
