using Azure.Messaging.ServiceBus;
using System.Text.Json;

namespace RapidRedPanda.ISBM.Host.Services;

public sealed record PublicationMessageEnvelope(
    Guid PublicationId,
    ServiceBusMessage Message,
    IReadOnlyList<string> Topics);

public static class PublicationMessageFactory
{
    public static PublicationMessageEnvelope CreateMessage(BinaryData payload, IReadOnlyList<string> topics)
    {
        return CreateMessage(Guid.NewGuid(), payload, topics);
    }

    public static PublicationMessageEnvelope CreatePublicationMessage(BinaryData messageContent, IReadOnlyList<string> topics)
    {
        Guid publicationId = Guid.NewGuid();
        BinaryData payload = BinaryData.FromBytes(CreatePublicationEnvelopeBody(publicationId, messageContent, topics));
        return CreateMessage(publicationId, payload, topics);
    }

    private static PublicationMessageEnvelope CreateMessage(Guid publicationId, BinaryData payload, IReadOnlyList<string> topics)
    {
        if (topics.Count == 0)
        {
            throw new ArgumentException("At least one publication topic is required.", nameof(topics));
        }

        IReadOnlyList<string> deduplicatedTopics = TopicSelectionRules.DeduplicateTopics(topics);
        if (deduplicatedTopics.Count > PublicationRoutingProperties.MaxPublicationTopics)
        {
            throw new ArgumentOutOfRangeException(
                nameof(topics),
                $"A publication can contain at most {PublicationRoutingProperties.MaxPublicationTopics} distinct topics.");
        }

        ServiceBusMessage message = new(payload)
        {
            MessageId = publicationId.ToString("D")
        };

        message.ApplicationProperties[PublicationRoutingProperties.PublicationId] = publicationId.ToString("D");
        message.ApplicationProperties[PublicationRoutingProperties.TopicCount] = deduplicatedTopics.Count;
        message.ApplicationProperties[PublicationRoutingProperties.TopicKeySet] = TopicSelectionRules.BuildTopicKeySet(deduplicatedTopics);

        for (int index = 0; index < deduplicatedTopics.Count; index++)
        {
            message.ApplicationProperties[PublicationRoutingProperties.TopicPropertyName(index)] = deduplicatedTopics[index];
        }

        return new PublicationMessageEnvelope(publicationId, message, deduplicatedTopics);
    }

    private static byte[] CreatePublicationEnvelopeBody(Guid publicationId, BinaryData messageContent, IReadOnlyList<string> topics)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("messageId", publicationId.ToString("D"));
            writer.WritePropertyName("messageContent");
            using (JsonDocument messageContentDocument = JsonDocument.Parse(messageContent))
            {
                messageContentDocument.RootElement.WriteTo(writer);
            }

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
}
