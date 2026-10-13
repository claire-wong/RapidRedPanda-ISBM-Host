# Observe a Publication Notification

Add an optional listener to [Complete REST Publication Tutorial](first-message.md). The listener observes a notice; the Consumer still reads and removes the publication through the Host.

**Source-reviewed draft, not live validated.** This PowerShell 7 example is Windows-oriented and local-only. It has no authentication, durable storage, concurrency handling, or production hardening.

## 1. Prepare Publisher and Consumer

Use the same-machine development setup from the [Complete REST Publication Tutorial](first-message.md). Complete its steps 1-3 in the Publisher and Consumer windows. Do not publish yet.

In Consumer, replace that tutorial's step 4 with:

```powershell
$IsbmChannelUri = (Read-Host 'Channel URI displayed by Publisher').Trim()
if ([string]::IsNullOrWhiteSpace($IsbmChannelUri)) { throw 'A channel URI is required.' }
$IsbmChannelRoute = [uri]::EscapeDataString($IsbmChannelUri)
$channel = Invoke-Isbm -Method Get -Path "channels/$IsbmChannelRoute" -ExpectedStatus 200
if ($channel.channelType -ne 'Publication') { throw 'Use a Publication channel.' }

$subscriptionBody = @{
    topics = @($IsbmTopic)
    listenerUrl = 'http://localhost:8787/listener/'
} | ConvertTo-Json -Depth 10
$consumerSession = Invoke-Isbm -Method Post -Path "channels/$IsbmChannelRoute/subscription-sessions" -Body $subscriptionBody -ExpectedStatus 201
$consumerSessionId = $consumerSession.sessionId
if ([string]::IsNullOrWhiteSpace($consumerSessionId)) { throw 'No consumer session ID was returned.' }
Write-Host "Listener session: $consumerSessionId"
```

This address must be reachable from the Host. Loopback works here only because the Host and listener run on the same machine. Host API calls still use trusted HTTPS; the incoming HTTPS guard does not prohibit this outgoing HTTP callback.

## 2. Start the Local Listener

Open a third PowerShell 7 window called Listener. Enter the Consumer session ID when prompted. Start listening **before** Publisher posts, and do not read in Consumer until the notice has been observed: reading can suppress a pending publication notice.

```powershell
$ErrorActionPreference = 'Stop'
$expectedSession = [guid]::Parse((Read-Host 'Consumer session ID').Trim())
$seen = [Collections.Generic.HashSet[string]]::new()
$listener = [Net.HttpListener]::new()
$listener.Prefixes.Add('http://localhost:8787/listener/')
try {
    $listener.Start()
    Write-Host 'Listening on http://localhost:8787/listener/'
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        $request = $context.Request
        $response = $context.Response
        try {
            $response.StatusCode = 400
            if ($request.HttpMethod -ne 'PUT') {
                $response.StatusCode = 405
                continue
            }
            $match = [regex]::Match($request.Url.AbsolutePath, '^/listener/notifications/([0-9a-fA-F-]{36})/([0-9a-fA-F-]{36})$')
            $sessionId = [guid]::Empty
            $messageId = [guid]::Empty
            if (-not $match.Success -or
                -not [guid]::TryParse($match.Groups[1].Value, [ref]$sessionId) -or
                -not [guid]::TryParse($match.Groups[2].Value, [ref]$messageId) -or
                $sessionId -ne $expectedSession) {
                $response.StatusCode = 404
                continue
            }
            if ([string]::IsNullOrWhiteSpace($request.ContentType) -or
                $request.ContentType.Split(';')[0].Trim() -ne 'application/json') {
                $response.StatusCode = 415
                continue
            }

            # Bound reads even when the sender does not provide Content-Length.
            $buffer = [byte[]]::new(8193)
            $length = 0
            while ($length -lt $buffer.Length) {
                $read = $request.InputStream.Read($buffer, $length, $buffer.Length - $length)
                if ($read -eq 0) { break }
                $length += $read
            }
            if ($length -gt 8192) {
                $response.StatusCode = 413
                continue
            }
            $body = [Text.Encoding]::UTF8.GetString($buffer, 0, $length) | ConvertFrom-Json -AsHashtable
            if ($body -isnot [Collections.IDictionary] -or
                -not $body.Contains('topics') -or
                $body.topics -isnot [Collections.IList] -or
                $body.topics.Count -eq 0) {
                continue
            }
            $validTopics = $true
            foreach ($topic in $body.topics) {
                if ($topic -isnot [string] -or [string]::IsNullOrWhiteSpace($topic)) {
                    $validTopics = $false
                }
            }
            if (-not $validTopics) { continue }

            $key = "$sessionId/$messageId"
            $isNew = $seen.Add($key)
            $response.StatusCode = 204
        } catch {
            $response.StatusCode = 400
        } finally {
            $response.Close()
        }
        if ($response.StatusCode -eq 204) {
            if ($isNew) {
                Write-Host "Availability notice: session=$sessionId message=$messageId"
            } else {
                Write-Host "Duplicate notice accepted: message=$messageId"
            }
        }
    }
} finally {
    $listener.Close()
}
```

Expected callback path: `/listener/notifications/{consumerSessionId}/{publicationMessageId}`, with a JSON topics array. Only **204** counts as delivered; 200 is a terminal failure in the Host callback client.

The session check reduces accidental unrelated notices; it is **not authentication**. This sample accepts publication-shaped notices only, not response bodies. It remembers identifiers in memory and acknowledges observation, not durable business acceptance. It does not retrieve the publication or run application processing.

If binding fails because the port is occupied or URL reservation permission is missing, stop and ask the operator to approve the exact local binding/reservation or use an existing application listener. Do not broaden the binding, elevate routinely, or change firewalls to make this demonstration work.

## 3. Publish, Read, and Clean Up

Once Listener says it is listening, run step 5 of the [Complete REST Publication Tutorial](first-message.md) in Publisher. Expect a notice containing the same publication ID. This is an expected result, not a recorded runtime result.

Then complete steps 6-8 of the [Complete REST Publication Tutorial](first-message.md): Consumer reads the business payload, removes it, closes its session, and clears credential variables. Publisher closes its session and deletes only its demonstration channel.

Stop Listener with Ctrl+C; close its window if the blocking accept does not return. Closing the window loses deduplication state. No callback means neither proof of an empty subscription nor permission to repost the business message blindly: use bounded Host polling and sanitized diagnostics.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| No notice | Dispatcher enabled, listener ready before posting, correct session/base URL, loopback on the Host machine, matching topics, and publication not already read or expired. |
| Callback stops after a non-204 result | Most statuses are terminal. Correct the listener; do not assume failed records are automatically replayed. |
| Duplicate notice | Acknowledge safely accepted duplicates with 204; make real processing idempotent. |
| Notice arrives but Host read is empty | Availability can race with reads, expiry, or removal. Interpret the operation's actual fault/status. |
| Callback times out | Return promptly. This single-threaded observation sample is not a production queue or hardened HTTP server. |

For all three callback bodies, retry/deadline rules, and security boundaries, read [Notifications](../api/notifications.md). Return to [Getting Started](README.md).

