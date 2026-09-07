using System.Text.Json;

namespace RapidRedPanda.ISBM.Host.Messaging;

internal static class RestMessageContentMapper
{
    private const string Base64ContentEncoding = "base64";

    public static bool TryFromRest(JsonElement messageContent, out InternalMessageContent? content)
    {
        content = null;
        if (messageContent.ValueKind is not JsonValueKind.Object ||
            !messageContent.TryGetProperty("content", out JsonElement contentElement))
        {
            return false;
        }

        if (!TryReadOptionalString(messageContent, "mediaType", out string? mediaType) ||
            !TryReadOptionalString(messageContent, "contentEncoding", out string? contentEncoding))
        {
            return false;
        }

        try
        {
            if (contentElement.ValueKind is JsonValueKind.Object)
            {
                if (mediaType is not null || contentEncoding is not null)
                {
                    return false;
                }

                content = InternalMessageContent.FromJson(BinaryData.FromString(contentElement.GetRawText()));
                return true;
            }

            if (contentElement.ValueKind is JsonValueKind.String && contentEncoding is null)
            {
                if (mediaType is null)
                {
                    return false;
                }

                content = InternalMessageContent.FromString(contentElement.GetString()!, mediaType);
                return true;
            }

            if (contentElement.ValueKind is JsonValueKind.String && contentEncoding is not null)
            {
                if (!IsSupportedBinaryEncoding(contentEncoding))
                {
                    return false;
                }

                byte[] decodedPayload = Convert.FromBase64String(contentElement.GetString()!);
                content = InternalMessageContent.FromBinary(
                    BinaryData.FromBytes(decodedPayload),
                    mediaType,
                    Base64ContentEncoding);
                return true;
            }
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }

        return false;
    }

    public static JsonElement ToRestJsonElement(InternalMessageContent content)
    {
        using JsonDocument document = JsonDocument.Parse(ToRestJsonString(content));
        return document.RootElement.Clone();
    }

    public static string ToRestJsonString(InternalMessageContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            WriteRestMessageContent(writer, content);
        }

        return BinaryData.FromBytes(stream.ToArray()).ToString();
    }

    public static void WriteRestMessageContent(Utf8JsonWriter writer, InternalMessageContent content)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(content);

        writer.WriteStartObject();
        switch (content.Kind)
        {
            case InternalMessageContentKind.Json:
                writer.WritePropertyName("content");
                using (JsonDocument payload = JsonDocument.Parse(content.Payload))
                {
                    payload.RootElement.WriteTo(writer);
                }

                break;

            case InternalMessageContentKind.String:
                writer.WriteString("mediaType", content.MediaType);
                writer.WriteString("content", content.Payload.ToString());
                break;

            case InternalMessageContentKind.Binary:
                if (content.MediaType is not null)
                {
                    writer.WriteString("mediaType", content.MediaType);
                }

                writer.WriteString("contentEncoding", Base64ContentEncoding);
                writer.WriteString("content", Convert.ToBase64String(content.Payload.ToArray()));
                break;

            default:
                throw new InvalidOperationException($"Unsupported message content kind '{content.Kind}'.");
        }

        writer.WriteEndObject();
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

    private static bool IsSupportedBinaryEncoding(string contentEncoding)
    {
        return string.Equals(contentEncoding, Base64ContentEncoding, StringComparison.OrdinalIgnoreCase);
    }
}
