using System.Text.Json;

namespace RapidRedPanda.ISBM.Host.Validation;

internal enum MessageContentKind
{
    Json,
    String,
    Encoded
}

internal sealed record MessageContentValidationResult(
    bool Succeeded,
    MessageContentKind? Kind,
    string? PayloadMediaType,
    string? ContentEncoding)
{
    public static MessageContentValidationResult Invalid { get; } = new(false, null, null, null);

    public static MessageContentValidationResult Valid(
        MessageContentKind kind,
        string? payloadMediaType,
        string? contentEncoding = null)
    {
        return new MessageContentValidationResult(true, kind, payloadMediaType, contentEncoding);
    }
}

internal static class MessageContentSemanticsValidator
{
    public static MessageContentValidationResult Validate(JsonElement messageContent)
    {
        if (messageContent.ValueKind is not JsonValueKind.Object ||
            !messageContent.TryGetProperty("content", out JsonElement content))
        {
            return MessageContentValidationResult.Invalid;
        }

        if (!TryReadOptionalString(messageContent, "mediaType", out string? mediaType) ||
            !TryReadOptionalString(messageContent, "contentEncoding", out string? contentEncoding))
        {
            return MessageContentValidationResult.Invalid;
        }

        return content.ValueKind switch
        {
            JsonValueKind.Object when mediaType is null && contentEncoding is null =>
                MessageContentValidationResult.Valid(MessageContentKind.Json, null),

            JsonValueKind.String when mediaType is not null && contentEncoding is null =>
                MessageContentValidationResult.Valid(MessageContentKind.String, mediaType),

            JsonValueKind.String when contentEncoding is not null =>
                MessageContentValidationResult.Invalid,

            _ => MessageContentValidationResult.Invalid
        };
    }

    private static bool TryReadOptionalString(JsonElement source, string propertyName, out string? value)
    {
        value = null;
        if (!source.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind is JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind is not JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }
}
