using Azure.Messaging.ServiceBus;
using RapidRedPanda.ISBM.Host.Messaging;
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
        InternalMessageContent internalContent;
        try
        {
            using JsonDocument document = JsonDocument.Parse(messageContent);
            if (!RestMessageContentMapper.TryFromRest(document.RootElement, out InternalMessageContent? content))
            {
                throw new ArgumentException("Message content must be a valid ISBM MessageContent value.", nameof(messageContent));
            }

            internalContent = content!;
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Message content must be a valid ISBM MessageContent value.", nameof(messageContent), exception);
        }

        BinaryData payload = BinaryData.FromBytes(BrokerMessageContentAdapter.CreateEnvelopeBody(publicationId, internalContent, topics));
        return CreateMessage(publicationId, payload, topics);
    }

    public static PublicationMessageEnvelope CreatePublicationMessage(InternalMessageContent messageContent, IReadOnlyList<string> topics)
    {
        Guid publicationId = Guid.NewGuid();
        BinaryData payload = BinaryData.FromBytes(BrokerMessageContentAdapter.CreateEnvelopeBody(publicationId, messageContent, topics));
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

}
