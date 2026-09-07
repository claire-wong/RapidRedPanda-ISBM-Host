using RapidRedPanda.ISBM.Host.Services;
using System.Text.Json;

namespace RapidRedPanda.ISBM.Host.Messaging;

internal static class BrokerMessageContentAdapter
{
    public static byte[] CreateEnvelopeBody(
        Guid messageId,
        InternalMessageContent messageContent,
        IReadOnlyList<string> topics)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("messageId", messageId.ToString("D"));
            writer.WritePropertyName("messageContent");
            RestMessageContentMapper.WriteRestMessageContent(writer, messageContent);
            writer.WritePropertyName("topics");
            writer.WriteStartArray();
            foreach (string topic in TopicSelectionRules.DeduplicateTopics(topics))
            {
                writer.WriteStringValue(topic);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static bool TryReadMessageContent(
        TransportPublication publication,
        out InternalMessageContent? messageContent)
    {
        messageContent = null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(publication.Content);
            JsonElement root = document.RootElement;
            if (root.ValueKind is JsonValueKind.Object &&
                root.TryGetProperty("messageId", out JsonElement messageIdElement) &&
                messageIdElement.ValueKind is JsonValueKind.String &&
                Guid.TryParse(messageIdElement.GetString(), out Guid bodyMessageId) &&
                bodyMessageId == publication.PublicationId &&
                root.TryGetProperty("messageContent", out JsonElement messageContentElement))
            {
                return RestMessageContentMapper.TryFromRest(messageContentElement, out messageContent);
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }
}
