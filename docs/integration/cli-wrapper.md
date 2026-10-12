# CLI Wrapper

Use this approach for automation, shell scripts, Python `subprocess` calls, and command-driven applications that want machine-friendly JSON output.

The Wrapper is an external project, not part of the Host distribution. Its basic architecture is:

```text
Application or script -> Wrapper CLI -> ClientAdapter -> ISBM Host
```

Python examples in the Wrapper repository invoke the CLI with `subprocess`. Python applications can also call the Host REST API directly; the CLI is optional.

## Minimal Example

From an extracted Windows Wrapper release package, open a provider publication session on an existing Publication channel and post one JSON message:

```powershell
$hostUrl = 'https://localhost:7443/isbm/2.0'
$channel = 'urn:rapidredpanda:demo:publication'
$topic = 'equipment.temperature'
$content = '{"equipmentId":"PUMP-101","temperature":72.5,"status":"Normal"}'

$open = .\RapidRedPanda.Wrapper.Cli.exe open-publication-session `
    --host $hostUrl `
    --channel $channel `
    --topic $topic `
    --user $env:ISBM_USER `
    --password $env:ISBM_PASSWORD | ConvertFrom-Json

if (-not $open.success) {
    throw "Open publication session failed."
}

$sessionId = $open.data.sessionId

$post = .\RapidRedPanda.Wrapper.Cli.exe post-publication `
    --host $hostUrl `
    --session-id $sessionId `
    --topic $topic `
    --content $content `
    --user $env:ISBM_USER `
    --password $env:ISBM_PASSWORD | ConvertFrom-Json

if (-not $post.success) {
    throw "Post publication failed."
}

"Publication session: $sessionId"
"Published message: $($post.data.messageId)"
```

The CLI command handles HTTP request construction and supported ISBM envelope formatting, then writes structured JSON output and process exit codes for automation. Applications still need to manage session IDs, operation failures, and publication lifetime. More detailed fault handling belongs in the Wrapper documentation.

## Session Cleanup

Run the close command only when remaining unexpired publications from this provider session may be expired. Under ISBM 2.0 Section 5.4.4, closing the provider publication session expires remaining unexpired publications posted during that session.

```powershell
.\RapidRedPanda.Wrapper.Cli.exe close-publication-session `
    --host $hostUrl `
    --session-id $sessionId `
    --user $env:ISBM_USER `
    --password $env:ISBM_PASSWORD
```

The CLI exposes the ISBM lifecycle as commands; it does not automatically manage session lifetime for you. Do not close immediately after posting if subscribers still need to claim the publication, and do not abandon sessions indefinitely.

## Requirements

- The Wrapper CLI must be installed, extracted from a release package, or built separately from [RapidRedPanda-ISBM-Wrapper](https://github.com/claire-wong/RapidRedPanda-ISBM-Wrapper).
- The Host must already be running at the `--host` URL.
- The Publication channel `urn:rapidredpanda:demo:publication` must already exist.
- The topic `equipment.temperature` must match the publisher/subscriber agreement.
- Matching channel credentials are required.
- An eligible subscription must already be open if you intend to verify retrieval.
- Publishing successfully does not prove that a subscriber received or processed the message.

## Credential Security

The example avoids a literal password by reading `$env:ISBM_PASSWORD`, but PowerShell still expands it into a `--password` process argument that may be visible through shell history, logs, or process inspection. Use the Wrapper's documented configuration and security guidance for shared or production environments, and prefer trusted HTTPS endpoints.

## Further Documentation

- [Wrapper README](https://github.com/claire-wong/RapidRedPanda-ISBM-Wrapper)
- [Wrapper Python samples](https://github.com/claire-wong/RapidRedPanda-ISBM-Wrapper/tree/main/samples/python)
- [Wrapper security guidance](https://github.com/claire-wong/RapidRedPanda-ISBM-Wrapper/blob/main/SECURITY.md)

The Wrapper is MIT licensed, documents version 0.3.5, targets .NET 8, and depends on `RapidRedPanda.ISBM.ClientAdapter` 2.2.0. Published Wrapper documentation does not, by itself, prove live compatibility with a particular Host staging build. Return to [Integration Options](README.md).
