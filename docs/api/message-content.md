# Message Content

ISBM's JSON request envelope carries both routing fields and an application payload. The Host provides messaging, not validation of CCOM or any other business schema.

**Source-reviewed draft:** these shapes are accepted by the current REST message mapper; no live transport round trip was performed for this batch.

## Envelope and Payload Are Different

A publication request looks like:

```json
{
  "topics": ["equipment.temperature"],
  "messageContent": {
    "content": {
      "equipmentId": "PUMP-101",
      "temperature": 72.5
    }
  },
  "expiry": "PT5M"
}
```

`topics` and `expiry` are publication fields. Your application's data is inside `messageContent.content`. Other message-posting operations reuse messageContent but have their own surrounding fields and routes.

Send the HTTP body as JSON, even when the payload is XML, plain text, or bytes. Use `Content-Type: application/json; charset=utf-8`; do not switch the envelope to `text/plain` or `application/xml` because of the inner content.

## Supported Representations

| Representation | `content` | `mediaType` | `contentEncoding` |
| --- | --- | --- | --- |
| Native JSON | Object | Omit | Omit |
| Typed text | JSON string | Required nonblank string | Omit |
| Bytes | Base64 JSON string | Optional nonblank string | `base64` |

Use the lower-camel-case member names exactly. The mapper looks up `content`, `mediaType`, and `contentEncoding` by these names even though some outer request deserializers accept case-insensitive properties.

### Native JSON

```json
{
  "content": {
    "equipmentId": "PUMP-101",
    "temperature": 72.5,
    "readings": [71.8, 72.5]
  }
}
```

Native content must be a JSON **object**, not a top-level array, number, Boolean, string, or null. Arrays/scalars can be members of that object.

Do not add mediaType or contentEncoding for native JSON. Explicit null metadata is treated as absent, but omission is clearer. Non-null metadata on a native object is rejected.

### Typed Text

```json
{
  "mediaType": "text/plain",
  "content": "PUMP-101 temperature is 72.5"
}
```

An XML document can similarly be a JSON string with an application-defined XML media type. The Host does not infer or validate the business schema from mediaType. Text is not Base64-encoded when contentEncoding is omitted.

A JSON document can be carried as typed text if your clients agree, but the receiver then gets a string rather than a native JSON object.

### Bytes

```json
{
  "mediaType": "application/octet-stream",
  "contentEncoding": "base64",
  "content": "SGVsbG8="
}
```

This encodes the bytes for `Hello`. The Host decodes the Base64 for internal handling and returns binary content encoded as Base64 in REST responses. Encoding comparison accepts case variations; output uses `base64`. Other encodings are not supported by this mapper.

MediaType can be omitted for bytes. If supplied, it must be a nonblank string. Base64 is an encoding, not confidentiality or compression.

## Rejected Shapes

The message-posting controller maps invalid content to HTTP 400 ParameterFault. Examples include:

- Missing/null messageContent or missing content.
- Native arrays/scalars/null instead of an object.
- String content without mediaType when contentEncoding is absent.
- Invalid Base64 or unsupported contentEncoding.
- Nonstring/blank metadata, or metadata on native JSON content.
- Raw application JSON sent without the messageContent envelope.

An invalid/missing envelope Content-Type can instead produce HTTP 415 before content mapping. Authorization and transport checks may reject the request earlier. See [Fault Handling](fault-handling.md).

## Application Responsibilities

Agree on schema/version, units, identifiers, and interpretation with the receiving application. CCOM is one optional payload agreement, not a Host requirement. A valid envelope is not proof that the receiver understands the business data.

No payload-size guarantee is established by this reference. Hosting and Azure Service Bus limits still apply; validate representative sizes in your deployment. Do not assume identical byte-level formatting of native JSON after serialization.

Continue with [Publication / Subscription](publication-workflow.md), [Compatibility](compatibility-and-limitations.md), or the [native JSON tutorial](../getting-started/first-message.md).

