# Your First Request and Response

Ask a responder for equipment status, then read a correlated JSON response. This uses application-defined content, not CCOM and not a synchronous RPC call.

**Source-reviewed draft, not end-to-end validated:** commands below have not been run against a live Host, database, or Azure Service Bus namespace.

## Before You Begin

Complete [Installation](installation.md) and [Configuration](configuration.md). Use a trusted HTTPS endpoint, a dedicated development database/namespace, and **PowerShell 7**.

Open two client windows named **Requester** and **Responder**, keeping the Host terminal separate. The requester uses a consumer request session; the responder uses a provider request session. This tutorial uses polling and needs no callback listener.

In **both** windows, run only the complete **Prepare Both Client Windows** block from [Your First Message](first-message.md). That block sets the HTTPS address, prompts for channel credentials, and defines Invoke-Isbm. Do **not** run its publication-channel/session steps.

Enter the same new private demonstration username/password in both windows. Use the same configured HTTPS address. Do not reuse SQL/Azure credentials or print headers/credential-bearing bodies.

Then run this block in **both** windows:

```powershell
$IsbmTopic = 'equipment.status.request'

function Wait-IsbmMessage {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $candidate = Invoke-Isbm -Method Get -Path $Path -ExpectedStatus @(200, 404)
        if ($candidate.fault -eq 'MessageFault') {
            Start-Sleep -Milliseconds 500
            continue
        }
        if ($candidate.fault) { throw "Read failed: $($candidate.fault)" }
        if ([string]::IsNullOrWhiteSpace($candidate.messageId)) {
            throw 'The read response did not contain a message ID.'
        }
        return $candidate
    }
    throw 'No message became available within the polling attempts. Check the request deadline, topic, role, and Host diagnostics.'
}
```

The helper retries only read-operation MessageFault. Other 404 faults are errors. Each read can wait on transport; this attempt limit is not an exact elapsed-time deadline.

## 1. Create a Request Channel

In **Requester**:

```powershell
$IsbmChannelUri = 'urn:rapidredpanda:demo:request:' + [guid]::NewGuid().ToString('N')
$channelBody = @{
    uri = $IsbmChannelUri
    channelType = 'Request'
    description = 'Request-response demonstration'
    securityTokens = @(
        @{
            type = 'UsernameToken'
            username = $IsbmCredential.UserName
            password = $IsbmCredential.GetNetworkCredential().Password
        }
    )
} | ConvertTo-Json -Depth 10

$createdChannel = Invoke-Isbm -Method Post -Path 'channels' -Body $channelBody -ExpectedStatus 201
Remove-Variable channelBody
$IsbmChannelUri = $createdChannel.uri
if ([string]::IsNullOrWhiteSpace($IsbmChannelUri)) { throw 'No channel URI was returned.' }
$IsbmChannelRoute = [uri]::EscapeDataString($IsbmChannelUri)
Write-Host "Channel URI for Responder: $IsbmChannelUri"
```

Creating tokens together with the channel makes it secured. A Publication channel cannot be substituted here. Delete only this unique demonstration channel during cleanup.

## 2. Open the Responder First

In **Responder**, enter the displayed channel URI:

```powershell
$IsbmChannelUri = (Read-Host 'Channel URI displayed by Requester').Trim()
if ([string]::IsNullOrWhiteSpace($IsbmChannelUri)) { throw 'A channel URI is required.' }
$IsbmChannelRoute = [uri]::EscapeDataString($IsbmChannelUri)

$channel = Invoke-Isbm -Method Get -Path "channels/$IsbmChannelRoute" -ExpectedStatus 200
if ($channel.channelType -ne 'Request') { throw 'This tutorial needs a Request channel.' }

$providerBody = @{ topics = @($IsbmTopic) } | ConvertTo-Json -Depth 10
$providerSession = Invoke-Isbm -Method Post -Path "channels/$IsbmChannelRoute/provider-request-sessions" -Body $providerBody -ExpectedStatus 201
$providerRequestSessionId = $providerSession.sessionId
if ([string]::IsNullOrWhiteSpace($providerRequestSessionId)) { throw 'No responder session ID was returned.' }
Write-Host "Responder ready: $providerRequestSessionId"
```

This creates a topic-selecting responder subscription. FilterExpressions and listenerUrl are omitted. Topic selection is case-sensitive; use the same value in both windows.

**Wait for Responder's ready message before posting the request.** Subscriptions opened later do not receive earlier broker messages retroactively.

## 3. Open the Requester and Send

In **Requester**:

```powershell
$consumerSession = Invoke-Isbm -Method Post -Path "channels/$IsbmChannelRoute/consumer-request-sessions" -Body '{}' -ExpectedStatus 201
$consumerRequestSessionId = $consumerSession.sessionId
if ([string]::IsNullOrWhiteSpace($consumerRequestSessionId)) { throw 'No requester session ID was returned.' }

$requestBody = @{
    topics = @($IsbmTopic)
    messageContent = @{
        content = @{
            equipmentId = 'PUMP-101'
            action = 'ReadStatus'
        }
    }
    expiry = 'PT30M'
} | ConvertTo-Json -Depth 10

$postedRequest = Invoke-Isbm -Method Post -Path "sessions/$consumerRequestSessionId/requests" -Body $requestBody -ExpectedStatus 201
$requestMessageId = $postedRequest.messageId
if ([string]::IsNullOrWhiteSpace($requestMessageId)) { throw 'No request message ID was returned.' }
Write-Host "Request message: $requestMessageId"
```

The empty requester-opening object means no response listener. Topics are on the request itself. Native JSON belongs inside messageContent.content, without mediaType/contentEncoding.

Expiry is a positive 30-minute duration for manual coordination, not the Host default. Finish the exchange before it elapses. If it expires, stop this example and clean up; do not blindly resend the same state-changing request.

## 4. Receive and Respond

In **Responder**:

```powershell
$receivedRequest = Wait-IsbmMessage -Path "sessions/$providerRequestSessionId/request"
$receivedRequestMessageId = $receivedRequest.messageId
Write-Host "Received request: $receivedRequestMessageId"
$receivedRequest.messageContent.content | ConvertTo-Json -Depth 10

if ($receivedRequest.messageContent.content.equipmentId -ne 'PUMP-101' -or
    $receivedRequest.messageContent.content.action -ne 'ReadStatus') {
    throw 'Unexpected request content; do not respond automatically.'
}
if ($receivedRequest.topics -notcontains $IsbmTopic) {
    throw 'The received request did not include the expected topic.'
}

$responseBody = @{
    messageContent = @{
        content = @{
            equipmentId = 'PUMP-101'
            status = 'Normal'
            temperature = 72.5
        }
    }
} | ConvertTo-Json -Depth 10

$postedResponse = Invoke-Isbm -Method Post -Path "sessions/$providerRequestSessionId/requests/$receivedRequestMessageId/responses" -Body $responseBody -ExpectedStatus 201
$responseMessageId = $postedResponse.messageId
if ([string]::IsNullOrWhiteSpace($responseMessageId)) { throw 'No response message ID was returned.' }
Write-Host "Posted response: $responseMessageId"
```

Compare the received request ID with Requester's displayed request ID. The URL uses that request ID; the returned response has a different ID.

The illustrative equipment response is generated here; it does not read a real device. A successful response is persisted by the Host, not returned directly to the requester in the original PostRequest call.

PostResponse 404 MessageFault means no response was created for an eligible request. Do not treat it as a normal empty read or blindly retry. Even a successful post cannot guarantee delivery after request expiry.

After the successful response post, in **Responder**:

```powershell
$null = Invoke-Isbm -Method Delete -Path "sessions/$providerRequestSessionId/request" -ExpectedStatus 204
```

This acknowledges this responder's outstanding request. There is no request ID in the removal route. It does not delete the correlation record or the response.

The existing diagram shows removal before response submission; that is permitted while the request is active. This tutorial keeps the request outstanding until submission succeeds. Neither choice makes an expired exchange indefinitely consumable.

## 5. Read and Remove the Response

In **Requester**:

```powershell
$receivedResponse = Wait-IsbmMessage -Path "sessions/$consumerRequestSessionId/requests/$requestMessageId/response"
if ($receivedResponse.requestMessageId -ne $requestMessageId) {
    throw 'The response did not correlate to this request.'
}
if ($receivedResponse.messageContent.content.equipmentId -ne 'PUMP-101' -or
    $receivedResponse.messageContent.content.status -ne 'Normal' -or
    $receivedResponse.messageContent.content.temperature -ne 72.5) {
    throw 'Unexpected response content.'
}
Write-Host "Received response: $($receivedResponse.messageId)"
$receivedResponse.messageContent.content | ConvertTo-Json -Depth 10

$null = Invoke-Isbm -Method Delete -Path "sessions/$consumerRequestSessionId/requests/$requestMessageId/response" -ExpectedStatus 204
```

Compare the received response ID with Responder's displayed ID. ReadResponse returns both requestMessageId and response messageId. Response removal uses the **request ID**, not the response ID.

Repeated reads return the outstanding response until it is removed. Multiple responses are possible; this demonstration creates/consumes one, not a guarantee of one response per request. Other clients should not use this disposable channel.

## 6. End the Exchange and Clean Up

After response processing/removal, in **Requester**:

```powershell
$null = Invoke-Isbm -Method Delete -Path "sessions/$consumerRequestSessionId/requests/$requestMessageId" -ExpectedStatus 204
$null = Invoke-Isbm -Method Delete -Path "sessions/$consumerRequestSessionId" -ExpectedStatus 204
```

The first call expires your request; the second closes the requester session. They are separate from RemoveResponse. Keep the channel until Responder has closed its session.

In **Responder**:

```powershell
$null = Invoke-Isbm -Method Delete -Path "sessions/$providerRequestSessionId" -ExpectedStatus 204
Remove-Variable IsbmCredential, IsbmHeaders, providerBody, responseBody
```

Closing the responder removes its Host-managed broker subscription. Then, in **Requester**, optionally delete only the channel created in step 1:

```powershell
$null = Invoke-Isbm -Method Delete -Path "channels/$IsbmChannelRoute" -ExpectedStatus 204
Remove-Variable IsbmCredential, IsbmHeaders, requestBody
```

Credentials are removed from variables, but this does not guarantee forensic memory erasure. Close the demonstration client windows when finished.

If a step fails, retain the known IDs and use matching credentials to close only successfully opened sessions. Do not delete an unfamiliar channel or repeat business operations merely to make cleanup scripts pass. CloseSession can return SessionFault for an already-closed session.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Responder reads only MessageFault | Responder opened before posting, identical topic, request posted successfully and not expired. |
| Requester reads SessionFault | Use the original consumer request session and its own request ID; another session or unknown request is not an empty read. |
| PostResponse returns 404 MessageFault | Correct request ID/channel, active request lifetime, and outstanding context. This is a failed post, not a poll result. |
| Response read remains MessageFault | Responder actually posted, requester uses its request ID, and the request is still eligible before its deadline. |
| 422 OperationFault | Request channel and requester/responder IDs are not interchangeable with Publication roles. |
| HTTPS-required 400 problem title | Use the trusted HTTPS endpoint; check trusted proxy scheme handling if applicable. |
| 500 or timeout on a post | Outcome can be uncertain. Inspect sanitized diagnostics before retrying; duplicates are possible. |

No end-to-end success is claimed by this tutorial. See [Request / Response Reference](../api/request-response-workflow.md), [Fault Handling](../api/fault-handling.md), and [Compatibility](../api/compatibility-and-limitations.md).

