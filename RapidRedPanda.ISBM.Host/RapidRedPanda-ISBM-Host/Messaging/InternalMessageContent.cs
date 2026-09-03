using System.Text.Json;

namespace RapidRedPanda.ISBM.Host.Messaging;

public enum InternalMessageContentKind
{
    Json,
    String,
    Binary
}

public sealed record InternalMessageContent
{
    private InternalMessageContent(
        InternalMessageContentKind kind,
        BinaryData payload,
        string? mediaType,
        string? contentEncoding)
    {
        Kind = kind;
        Payload = payload;
        MediaType = mediaType;
        ContentEncoding = contentEncoding;
    }

    public InternalMessageContentKind Kind { get; }

    public BinaryData Payload { get; }

    public string? MediaType { get; }

    public string? ContentEncoding { get; }

    public static InternalMessageContent FromJson(BinaryData jsonObjectPayload)
    {
        ArgumentNullException.ThrowIfNull(jsonObjectPayload);

        try
        {
            using JsonDocument document = JsonDocument.Parse(jsonObjectPayload);
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                throw new ArgumentException("JSON message content payload must be a JSON object.", nameof(jsonObjectPayload));
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("JSON message content payload must be valid JSON.", nameof(jsonObjectPayload), exception);
        }

        return new InternalMessageContent(InternalMessageContentKind.Json, jsonObjectPayload, null, null);
    }

    public static InternalMessageContent FromString(string content, string mediaType)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (string.IsNullOrWhiteSpace(mediaType))
        {
            throw new ArgumentException("String message content must specify a media type.", nameof(mediaType));
        }

        return new InternalMessageContent(
            InternalMessageContentKind.String,
            BinaryData.FromString(content),
            mediaType,
            null);
    }

    public static InternalMessageContent FromBinary(
        BinaryData decodedPayload,
        string? mediaType,
        string contentEncoding)
    {
        ArgumentNullException.ThrowIfNull(decodedPayload);

        if (string.IsNullOrWhiteSpace(contentEncoding))
        {
            throw new ArgumentException("Binary message content must specify a content encoding.", nameof(contentEncoding));
        }

        return new InternalMessageContent(
            InternalMessageContentKind.Binary,
            decodedPayload,
            string.IsNullOrWhiteSpace(mediaType) ? null : mediaType,
            contentEncoding);
    }
}
