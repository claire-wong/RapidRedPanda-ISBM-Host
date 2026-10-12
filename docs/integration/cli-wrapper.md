# CLI Wrapper

Use this approach for automation, shell scripts, Python `subprocess` calls, and command-driven applications that want machine-friendly JSON output.

The Wrapper is an external project, not part of the Host distribution. Its basic architecture is:

```text
Application or script -> Wrapper CLI -> ClientAdapter -> ISBM Host
```

Python examples in the Wrapper repository invoke the CLI with `subprocess`. Python applications can also call the Host REST API directly; the CLI is optional.

## Minimal Example

From an extracted Windows Wrapper release package, open a subscription on an existing Publication channel:

```powershell
.\RapidRedPanda.Wrapper.Cli.exe open-subscription --host https://localhost:7443/isbm/2.0 --channel urn:rapidredpanda:demo:publication --topic equipment.temperature --user $env:ISBM_USER --password $env:ISBM_PASSWORD
```

The command writes a JSON response. On success, the response includes fields such as `success`, `command`, `timestampUtc`, and `data.sessionId`.

## Requirements

- The Wrapper CLI must be installed, extracted from a release package, or built separately from [RapidRedPanda-ISBM-Wrapper](https://github.com/claire-wong/RapidRedPanda-ISBM-Wrapper).
- The Host must already be running at the `--host` URL.
- The Publication channel `urn:rapidredpanda:demo:publication` must already exist.
- The topic `equipment.temperature` must match the publisher/subscriber agreement.
- Matching channel credentials are required.
- Opening a subscription creates Host-managed session state. Close the session when you are done.

## Credential Security

The example avoids a literal password by reading `$env:ISBM_PASSWORD`, but the expanded command-line argument may still be visible through shell history, logs, or process inspection. Use the Wrapper's documented configuration and security guidance for shared or production environments, and prefer trusted HTTPS endpoints.

## Further Documentation

- [Wrapper README](https://github.com/claire-wong/RapidRedPanda-ISBM-Wrapper)
- [Wrapper Python samples](https://github.com/claire-wong/RapidRedPanda-ISBM-Wrapper/tree/main/samples/python)
- [Wrapper security guidance](https://github.com/claire-wong/RapidRedPanda-ISBM-Wrapper/blob/main/SECURITY.md)

The Wrapper is MIT licensed, documents version 0.3.5, targets .NET 8, and depends on `RapidRedPanda.ISBM.ClientAdapter` 2.2.0. Published Wrapper documentation does not, by itself, prove live compatibility with a particular Host staging build. Return to [Integration Options](README.md).