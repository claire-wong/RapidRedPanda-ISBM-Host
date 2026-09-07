using System.Text.Json;
using RapidRedPanda.ISBM.Host.Data;
using System.Text;

namespace RapidRedPanda.ISBM.Host.Messaging;

internal static class SqlMessageContentAdapter
{
    private const string Base64ContentEncoding = "base64";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static PersistedMessageContent ToSql(InternalMessageContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new PersistedMessageContent(
            RestMessageContentMapper.ToRestJsonString(content),
            content.MediaType,
            content.Payload.ToArray(),
            content.Kind.ToString(),
            content.Kind is InternalMessageContentKind.Binary ? Base64ContentEncoding : null);
    }

    public static bool TryFromSql(
        PersistedMessageContent persisted,
        out InternalMessageContent? content)
    {
        ArgumentNullException.ThrowIfNull(persisted);

        if (persisted.Payload is null &&
            persisted.PayloadKind is null &&
            persisted.ContentEncoding is null)
        {
            return TryFromSql(persisted.LegacyMessageContent, persisted.MediaType, out content);
        }

        content = null;
        if (persisted.Payload is null ||
            string.IsNullOrWhiteSpace(persisted.PayloadKind))
        {
            return false;
        }

        try
        {
            content = persisted.PayloadKind switch
            {
                nameof(InternalMessageContentKind.Json) when
                    persisted.MediaType is null &&
                    persisted.ContentEncoding is null =>
                    InternalMessageContent.FromJson(BinaryData.FromBytes(persisted.Payload)),

                nameof(InternalMessageContentKind.String) when
                    !string.IsNullOrWhiteSpace(persisted.MediaType) &&
                    persisted.ContentEncoding is null =>
                    InternalMessageContent.FromString(StrictUtf8.GetString(persisted.Payload), persisted.MediaType),

                nameof(InternalMessageContentKind.Binary) when
                    IsSupportedBinaryEncoding(persisted.ContentEncoding) &&
                    (persisted.MediaType is null || !string.IsNullOrWhiteSpace(persisted.MediaType)) =>
                    InternalMessageContent.FromBinary(
                        BinaryData.FromBytes(persisted.Payload),
                        persisted.MediaType,
                        Base64ContentEncoding),

                _ => null
            };
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        return content is not null;
    }

    public static bool TryFromSql(
        string serializedMessageContent,
        string? mediaType,
        out InternalMessageContent? content)
    {
        content = null;
        if (string.IsNullOrWhiteSpace(serializedMessageContent))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(serializedMessageContent);
            if (!RestMessageContentMapper.TryFromRest(document.RootElement, out InternalMessageContent? parsed))
            {
                return false;
            }

            if (!string.Equals(parsed!.MediaType, mediaType, StringComparison.Ordinal))
            {
                return false;
            }

            content = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsSupportedBinaryEncoding(string? contentEncoding)
    {
        return string.Equals(contentEncoding, Base64ContentEncoding, StringComparison.OrdinalIgnoreCase);
    }
}
