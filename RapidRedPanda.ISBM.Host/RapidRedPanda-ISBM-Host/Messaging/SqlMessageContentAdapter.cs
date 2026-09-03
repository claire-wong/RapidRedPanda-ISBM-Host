using System.Text.Json;

namespace RapidRedPanda.ISBM.Host.Messaging;

internal sealed record SqlMessageContent(string MessageContent, string? MediaType);

internal static class SqlMessageContentAdapter
{
    public static SqlMessageContent ToSql(InternalMessageContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new SqlMessageContent(RestMessageContentMapper.ToRestJsonString(content), content.MediaType);
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
}
