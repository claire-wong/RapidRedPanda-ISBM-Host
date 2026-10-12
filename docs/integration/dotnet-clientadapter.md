# .NET ClientAdapter

Use this approach when a C# or .NET application should call ISBM operations through a convenience library instead of constructing every REST request manually.

The ClientAdapter is an external package, not part of the Host distribution. This page shows one small publication example. Use the full [ClientAdapter Coding Guide](https://github.com/claire-wong/RapidRedPanda.ISBM.ClientAdapter.SDK.Samples/blob/main/Documents/RapidRedPanda-ISBM-Coding-Guide.md) for channel creation, subscriptions, reading and removing messages, request/response, notifications, fault handling, and complete C# workflows.

## Install

```powershell
dotnet add package RapidRedPanda.ISBM.ClientAdapter --version 2.2.0
```

`RapidRedPanda.ISBM.ClientAdapter` 2.2.0 is published on [NuGet](https://www.nuget.org/packages/RapidRedPanda.ISBM.ClientAdapter/2.2.0) and is MIT licensed. It is still an external dependency; verify package and compatibility requirements for your deployment.

## Minimal Example

This example opens a provider publication session on an existing Publication channel, posts one JSON message, prints the returned message identifier, and closes the session.

```csharp
using RapidRedPanda.ISBM.ClientAdapter;
using RapidRedPanda.ISBM.ClientAdapter.Enums;

string hostAddress = "https://localhost:7443/isbm/2.0";
string channelId = "urn:rapidredpanda:demo:publication";
string topic = "equipment.temperature";
string userName = Environment.GetEnvironmentVariable("ISBM_USER")!;
string password = Environment.GetEnvironmentVariable("ISBM_PASSWORD")!;

ProviderPublicationService service = new();
service.Authentication.AuthenticationSchemeType = AuthenticationSchemeType.Basic;
service.Credential.Username = userName;
service.Credential.Password = password;

var open = await service.OpenPublicationSessionAsync(hostAddress, channelId, CancellationToken.None);
if (open.StatusCode != 201) throw new InvalidOperationException($"Open failed: {open.StatusCode}");

string payload = "{\"equipmentId\":\"PUMP-101\",\"temperature\":72.5,\"status\":\"Normal\"}";
var post = await service.PostPublicationAsync(hostAddress, open.SessionID, topic, payload, CancellationToken.None);
if (post.StatusCode != 201) throw new InvalidOperationException($"Post failed: {post.StatusCode}");

Console.WriteLine($"Published message: {post.MessageID}");
await service.ClosePublicationSessionAsync(hostAddress, open.SessionID, CancellationToken.None);
```

## Requirements

- The Host must already be running at the `hostAddress` you use.
- The Publication channel `urn:rapidredpanda:demo:publication` must already exist.
- Matching channel credentials are required.
- The example assumes `ISBM_USER` and `ISBM_PASSWORD` environment variables contain those credentials.
- Publishing successfully does not prove that a subscriber received or processed the message.
- Use HTTPS for credential-bearing calls.

For the complete publish/read/remove lifecycle, use the Host's [first-message tutorial](../getting-started/first-message.md) or the ClientAdapter Coding Guide. Return to [Integration Options](README.md).