# Channel Management

A channel is an application-facing name for a publication or request exchange. Create it before opening messaging sessions.

**Source-reviewed draft:** requests and responses below are illustrative, not live results. All paths are relative to `/isbm/2.0`; `{channelUri}` means the encoded URI described in [API Overview](overview.md).

## Operations

| Operation | Method and path | Success | Body / response |
| --- | --- | --- | --- |
| List accessible channels | GET `channels` | 200 | Array of channel descriptions; may be empty. |
| Read a channel | GET `channels/{channelUri}` | 200 | One channel description. |
| Create a channel | POST `channels` | 201 | Creation body; returns channel description. |
| Delete a channel | DELETE `channels/{channelUri}` | 204 | No body. |
| Add tokens | POST `channels/{channelUri}/security-tokens` | 201 | Token collection; no response document. |
| Remove tokens | DELETE `channels/{channelUri}/security-tokens` | 204 | Token collection in the DELETE body; no response document. |

Listing includes unsecured channels and secured channels accessible with the supplied Basic credentials. It is not an unrestricted inventory of every secured channel. Channel reads/deletes/token mutations require matching credentials when active tokens exist.

## Create a Secured Channel

Send `Content-Type: application/json`. Replace the password placeholder with a new private development password; do not copy it as a real credential or log the request.

```json
{
  "uri": "urn:rapidredpanda:example:temperature",
  "channelType": "Publication",
  "description": "Equipment temperature exchange",
  "securityTokens": [
    {
      "type": "UsernameToken",
      "username": "example-client",
      "password": "<choose-a-private-development-password>"
    }
  ]
}
```

Expected HTTP 201 response:

```json
{
  "uri": "urn:rapidredpanda:example:temperature",
  "channelType": "Publication",
  "description": "Equipment temperature exchange"
}
```

The response does not return credentials, security-token details, an internal channel GUID, or a broker topic name.

| Field | Current validation / behavior |
| --- | --- |
| `uri` | Required nonempty string, maximum 512 characters; no leading/trailing whitespace or control characters. The current validator does not require an absolute URL. Use a stable application URI. |
| `channelType` | Exactly `Publication` or `Request`, case-sensitive. Publication sessions require Publication channels. |
| `description` | Optional; maximum 2000 characters. |
| `securityTokens` | Optional collection. Omitted/null/empty means no tokens, not generated credentials. Supply tokens at creation for a secured channel. |

Creation provisions a Host-managed Azure Service Bus topic and records the channel in SQL Server. Provisioning failures can return OperationFault. SQL and broker work are not one atomic transaction; a failed operation may require operator inspection.

There is no channel-update endpoint in this reference. Do not assume PUT/PATCH changes to channel type, URI, or description.

## UsernameToken Shapes

Only UsernameToken is supported. The flat shape above is the recommended example. Omitting `type` from a supplied flat username/password token defaults its type to `UsernameToken`; it does not default either credential.

A nested shape is also parsed:

```json
{
  "securityTokens": [
    {
      "UsernameToken": {
        "username": "example-client",
        "password": "<choose-a-private-development-password>"
      }
    }
  ]
}
```

This is a token-collection body, not a complete CreateChannel request. Do not mix flat fields and a nested UsernameToken in the same entry. Unsupported shapes/types, missing credentials, and whitespace-only credentials are rejected.

Usernames are trimmed and normalized for matching, with a maximum of 256 characters. Duplicate entries for a normalized username with the same password are deduplicated; different passwords for the same normalized username are invalid.

## Token Changes and Access Safety

Add/remove bodies use `{"securityTokens":[...]}` with valid UsernameToken entries. Authorize the operation using an existing active channel credential.

- Adding tokens to a channel with **no active tokens** returns `409 OperationFault`. Create secured channels with tokens from the beginning.
- Adding an already active username does not replace its password. Do not treat AddSecurityTokens as a password-update endpoint.
- Removal requires a structurally valid token entry, including a password field, but the current repository selects removal targets by normalized username. Authorization uses the Basic header independently; the removal-body password is not proof of authority.
- Removing a requested username that is not active returns `409 SecurityTokenFault`.
- **Removing the last active token leaves the channel unsecured.** Adding tokens afterward is blocked by the open-channel rule. Do not use this as a rotation technique; it can expose subsequent channel/session access.

Removing a token can invalidate sessions bound to that identity while other tokens remain active. Keep the necessary active credential until its messaging sessions are closed. Last-token removal removes the channel credential boundary rather than safely locking the channel.

## Deleting a Channel

Close your sessions and finish processing first. Deletion removes the Host-managed broker topic before recording channel deletion in SQL; it can disrupt every application using the channel. It is not a single-message acknowledgment, and it does not guarantee transactional rollback across broker and database.

Delete only a dedicated example channel you created. A second delete can return ChannelFault; 204 is not a general promise of idempotent resource deletion.

## Errors

| Condition | Source-derived outcome |
| --- | --- |
| Invalid creation fields / channel URI / malformed JSON | 400 ParameterFault |
| Unsupported or invalid token entries | 409 SecurityTokenFault |
| Duplicate channel URI | 409 ChannelFault |
| Missing or unauthorized channel | 404 ChannelFault |
| Add tokens to an unsecured channel | 409 OperationFault |
| Requested removal token not found | 409 SecurityTokenFault |
| SQL/broker or unexpected operation failure | 500 OperationFault |

Transport/framework errors have other shapes. See [Fault Handling](fault-handling.md), [Sessions](sessions.md), and the [first-message tutorial](../getting-started/first-message.md).

