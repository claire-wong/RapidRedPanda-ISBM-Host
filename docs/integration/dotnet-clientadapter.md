# .NET ClientAdapter

Use this approach when a C# or .NET application should call ISBM operations through a convenience library instead of constructing every REST request manually.

The ClientAdapter is an external package, not part of the Host distribution. This page shows one small publication example. Use the full [ClientAdapter Coding Guide](https://github.com/claire-wong/RapidRedPanda.ISBM.ClientAdapter.SDK.Samples/blob/main/Documents/RapidRedPanda-ISBM-Coding-Guide.md) for channel creation, subscriptions, reading and removing messages, request/response, notifications, fault handling, and complete C# workflows.

## Install

```powershell
dotnet add package RapidRedPanda.ISBM.ClientAdapter --version 2.2.0
```

`RapidRedPanda.ISBM.ClientAdapter` 2.2.0 is published on [NuGet](https://www.nuget.org/packages/RapidRedPanda.ISBM.ClientAdapter/2.2.0) and is MIT licensed. It is still an external dependency; verify package and compatibility requirements for your deployment.

## Minimal Example

The Host must already be running, the Publication channel must already exist, and the credentials in `ISBM_USER` and `ISBM_PASSWORD` must be valid for that channel. This example demonstrates the publication side only: it opens a provider publication session, posts one JSON message, and displays the returned session and message identifiers.

```csharp
using RapidRedPanda.ISBM.ClientAdapter;
using RapidRedPanda.ISBM.ClientAdapter.Enums;
using RapidRedPanda.ISBM.ClientAdapter.ResponseType;

string hostAddress = "https://localhost:7443/isbm/2.0";
string channelId = "urn:rapidredpanda:demo:publication";

ProviderPublicationService publicationProvider = new();

publicationProvider.Authentication.AuthenticationSchemeType =
    AuthenticationSchemeType.Basic;

publicationProvider.Credential.Username =
    Environment.GetEnvironmentVariable("ISBM_USER")!;

publicationProvider.Credential.Password =
    Environment.GetEnvironmentVariable("ISBM_PASSWORD")!;

OpenPublicationSessionResponse openResponse =
    publicationProvider.OpenPublicationSession(hostAddress, channelId);

PostPublicationResponse postResponse = publicationProvider.PostPublication(
    hostAddress,
    openResponse.SessionID,
    "equipment.temperature",
    "{\"equipmentId\":\"PUMP-101\",\"temperature\":72.5,\"status\":\"Normal\"}");

Console.WriteLine($"Publication session: {openResponse.SessionID}");
Console.WriteLine($"Published message: {postResponse.MessageID}");
```

The ClientAdapter handles HTTP request construction, supported ISBM message-envelope formatting, and typed response deserialization. Your application still manages session lifetime and should check operation responses. Error handling is kept out of this introductory example for readability; use the full ClientAdapter Coding Guide for production-oriented response handling and asynchronous examples.

## Session Cleanup

Close the provider publication session only when remaining unexpired publications from that session may be expired. Under ISBM 2.0 Section 5.4.4, closing the provider publication session expires remaining unexpired publications posted during that session.

```csharp
publicationProvider.ClosePublicationSession(hostAddress, openResponse.SessionID);
```

Do not close immediately after posting if subscribers still need to claim the publication. Do not abandon sessions indefinitely either; production applications should use an intentional session-lifetime and publication-expiration policy.

## Requirements

- The Host must already be running at the `hostAddress` you use.
- The Publication channel `urn:rapidredpanda:demo:publication` must already exist.
- Matching channel credentials are required.
- The example assumes `ISBM_USER` and `ISBM_PASSWORD` environment variables contain those credentials.
- An eligible subscription must already be open if you intend to verify retrieval.
- Publishing successfully does not prove that a subscriber received or processed the message.
- Use HTTPS for credential-bearing calls.

For the complete publish/read/remove lifecycle, use the Host's [first-message tutorial](../getting-started/first-message.md) or the ClientAdapter Coding Guide. Return to [Integration Options](README.md).
