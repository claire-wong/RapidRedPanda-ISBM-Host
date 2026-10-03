using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Messaging;
using RapidRedPanda.ISBM.Host.Services;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class ProviderConsumerPublicationRoundTripTests
{
    public static IEnumerable<object?[]> MessageContentRoundTripCases()
    {
        yield return
        [
            """
            {
              "content": {
                "$schema": "http://www.mimosa.org/ccom4/BOD/SyncMeasurements.json",
                "syncMeasurements": {
                  "releaseID": "9.0",
                  "versionID": "1.0"
                }
              }
            }
            """,
            null
        ];
        yield return
        [
            """
            {
              "mediaType": "application/xml",
              "content": "<speed>1200</speed>"
            }
            """,
            "application/xml"
        ];
        yield return
        [
            """
            {
              "mediaType": "text/plain",
              "content": "speed=1200"
            }
            """,
            "text/plain"
        ];
        yield return
        [
            """
            {
              "mediaType": "application/octet-stream",
              "contentEncoding": "base64",
              "content": "AAECAwQ="
            }
            """,
            "application/octet-stream"
        ];
    }

    [Theory]
    [MemberData(nameof(MessageContentRoundTripCases))]
    public async Task ProviderPostWithoutExpiry_CanBeReadByConsumerWithSameMessageContentEnvelope(
        string messageContent,
        string? payloadMediaType)
    {
        Guid channelId = Guid.NewGuid();
        Guid consumerSessionId = Guid.NewGuid();
        const string azureTopicName = "isbm-topic";
        FakeChannelRepository channelRepository = new(channelId, azureTopicName);
        FakeProviderPublicationSessionRepository providerSessionRepository = new();
        CapturingProviderPublicationRepository publicationRepository = new();
        CapturingPublicationTransport publicationTransport = new();
        ProviderPublicationSessionService providerService = new(
            channelRepository,
            providerSessionRepository,
            publicationRepository,
            publicationTransport,
            NullLogger<ProviderPublicationSessionService>.Instance);

        OpenPublicationSessionResult openResult = await providerService.OpenPublicationSessionAsync(
            "%2Fpublication%2Fchannel",
            null,
            CancellationToken.None);
        Assert.True(openResult.Succeeded);

        PostPublicationResult postResult = await providerService.PostPublicationAsync(
            openResult.SessionId!.Value.ToString(),
            ["Topic-A"],
            ToInternalMessageContent(messageContent),
            null,
            CancellationToken.None);

        Assert.True(postResult.Succeeded);
        ServiceBusMessage sentMessage = Assert.Single(publicationTransport.SentMessages);
        Assert.Equal(new ServiceBusMessage().TimeToLive, sentMessage.TimeToLive);

        ConsumerSubscriptionSessionService consumerService = new(
            channelRepository,
            new FakeSubscriptionSessionRepository(consumerSessionId, channelId, azureTopicName),
            publicationRepository,
            new CapturingSubscriptionTransport(sentMessage),
            new NoOpChannelTopicTransport(),
            NullLogger<ConsumerSubscriptionSessionService>.Instance);

        ReadPublicationResult readResult = await consumerService.ReadPublicationAsync(
            consumerSessionId.ToString(),
            CancellationToken.None);

        Assert.True(readResult.Succeeded);
        Assert.True(readResult.HasPublication);
        Assert.NotNull(readResult.Publication);
        Assert.Equal(postResult.PublicationId, readResult.Publication.MessageId);
        Assert.Equal(["Topic-A"], readResult.Publication.Topics);
        Assert.Equal(payloadMediaType, sentMessage.ContentType);
        string restMessageContent = RestMessageContentMapper.ToRestJsonString(readResult.Publication.MessageContent);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(messageContent), JsonNode.Parse(restMessageContent)));
        if (readResult.Publication.MessageContent.Kind is InternalMessageContentKind.Binary)
        {
            Assert.Equal([0, 1, 2, 3, 4], readResult.Publication.MessageContent.Payload.ToArray());
        }

        Assert.DoesNotContain("expiry", restMessageContent, StringComparison.Ordinal);
    }

    private static InternalMessageContent ToInternalMessageContent(string messageContent)
    {
        using JsonDocument document = JsonDocument.Parse(messageContent);
        Assert.True(RestMessageContentMapper.TryFromRest(document.RootElement, out InternalMessageContent? internalMessageContent));
        return internalMessageContent!;
    }

    private sealed class FakeChannelRepository(Guid channelId, string azureTopicName) : IChannelRepository
    {
        public Task<IReadOnlyList<ChannelData>> GetChannelsAsync(ChannelCredentials? credentials, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ChannelData>>([]);
        }

        public Task<GetChannelResult> GetChannelAsync(string channelUri, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetChannelResult.Failure(GetChannelFailureKind.NotFound, "missing"));
        }

        public Task<GetChannelTopicBindingResult> GetChannelTopicBindingAsync(string channelUri, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetChannelTopicBindingResult.Failure(GetChannelTopicBindingFailureKind.NotFound, "missing"));
        }

        public Task<GetChannelTopicBindingResult> GetChannelTopicBindingByIdAsync(Guid requestedChannelId, CancellationToken cancellationToken)
        {
            if (requestedChannelId != channelId)
            {
                return Task.FromResult(GetChannelTopicBindingResult.Failure(GetChannelTopicBindingFailureKind.NotFound, "missing"));
            }

            return Task.FromResult(GetChannelTopicBindingResult.Success(new ChannelTopicBindingData(
                channelId,
                "/publication/channel",
                "Publication",
                azureTopicName)));
        }

        public Task<GetChannelForSubscriptionResult> GetChannelForConsumerPublicationSubscriptionAsync(
            string channelUri,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(GetChannelForSubscriptionResult.Success(new ChannelSubscriptionData(
                channelId,
                channelUri,
                "Publication",
                azureTopicName,
                null)));
        }

        public Task<GetChannelForSubscriptionResult> GetChannelForProviderPublicationSessionAsync(
            string channelUri,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(GetChannelForSubscriptionResult.Success(new ChannelSubscriptionData(
                channelId,
                channelUri,
                "Publication",
                azureTopicName,
                null)));
        }

        public Task<CreateChannelResult> CreateChannelAsync(ChannelCreateRequestData request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<DeleteChannelResult> DeleteChannelAsync(string channelUri, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<SecurityTokenMutationResult> AddSecurityTokensAsync(string channelUri, IReadOnlyList<ChannelCreateToken> tokens, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<SecurityTokenMutationResult> RemoveSecurityTokensAsync(string channelUri, IReadOnlyList<ChannelCreateToken> tokens, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeProviderPublicationSessionRepository : IProviderPublicationSessionRepository
    {
        private ProviderPublicationSessionData? _session;

        public Task<InsertProviderPublicationSessionResult> InsertProviderPublicationSessionAsync(
            ProviderPublicationSessionCreateData session,
            CancellationToken cancellationToken)
        {
            _session = new ProviderPublicationSessionData(
                session.SessionId,
                session.ChannelId,
                ProviderPublicationSessionStates.Open,
                session.AuthenticatedTokenUsername,
                DateTime.UtcNow,
                null);
            return Task.FromResult(InsertProviderPublicationSessionResult.Success());
        }

        public Task<GetProviderPublicationSessionResult> GetProviderPublicationSessionAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            if (_session is null || _session.SessionId != sessionId)
            {
                return Task.FromResult(GetProviderPublicationSessionResult.Failure(GetProviderPublicationSessionFailureKind.NotFound, "missing"));
            }

            return Task.FromResult(GetProviderPublicationSessionResult.Success(_session));
        }

        public Task<CloseProviderPublicationSessionResult> CloseProviderPublicationSessionAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeSubscriptionSessionRepository(Guid sessionId, Guid channelId, string azureTopicName) : ISubscriptionSessionRepository
    {
        public Task<InsertSubscriptionSessionResult> InsertConsumerSubscriptionSessionAsync(
            ConsumerSubscriptionSessionCreateData session,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<GetSubscriptionSessionResult> GetSubscriptionSessionAsync(Guid requestedSessionId, CancellationToken cancellationToken)
        {
            if (requestedSessionId != sessionId)
            {
                return Task.FromResult(GetSubscriptionSessionResult.Failure(GetSubscriptionSessionFailureKind.NotFound, "missing"));
            }

            return Task.FromResult(GetSubscriptionSessionResult.Success(new ConsumerSubscriptionSessionData(
                sessionId,
                channelId,
                SubscriptionSessionTypes.ConsumerPublicationSubscription,
                SubscriptionSessionStates.Open,
                azureTopicName,
                $"isbm-css-{sessionId:N}",
                ["Topic-A"],
                null,
                DateTime.UtcNow,
                null)));
        }

        public Task<CloseSubscriptionSessionResult> CloseSubscriptionSessionAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class CapturingPublicationTransport : IPublicationTransport
    {
        public List<ServiceBusMessage> SentMessages { get; } = [];

        public Task SendPublicationAsync(string topicName, ServiceBusMessage message, CancellationToken cancellationToken)
        {
            SentMessages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingProviderPublicationRepository : IProviderPublicationRepository
    {
        private readonly Dictionary<Guid, ProviderPublicationData> _publications = [];
        private readonly Dictionary<Guid, ProviderPublicationData> _outstanding = [];

        public Task<ProviderPublicationRepositoryResult> InsertPublishedPublicationAsync(
            ProviderPublicationCreateData publication,
            CancellationToken cancellationToken)
        {
            _publications[publication.MessageId] = new ProviderPublicationData(
                publication.MessageId,
                publication.ProviderSessionId,
                publication.ChannelId,
                publication.Topics,
                publication.MessageContent);
            return Task.FromResult(ProviderPublicationRepositoryResult.Success());
        }

        public Task<ProviderPublicationRepositoryResult> ExpirePublicationAsync(Guid providerSessionId, Guid channelId, Guid messageId, DateTime expiredUtc, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PublicationExpirationStateResult> IsPublicationExpiredAsync(Guid messageId, Guid consumerSessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(PublicationExpirationStateResult.Success(false));
        }

        public Task<ProviderPublicationRepositoryResult> MarkRecipientOutstandingAsync(Guid messageId, Guid consumerSessionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ReadProviderPublicationResult> GetOutstandingPublicationAsync(Guid consumerSessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(
                _outstanding.TryGetValue(consumerSessionId, out ProviderPublicationData? publication)
                    ? ReadProviderPublicationResult.Success(publication)
                    : ReadProviderPublicationResult.NoPublication());
        }

        public Task<ReadProviderPublicationResult> TryMarkPublicationOutstandingAsync(Guid messageId, Guid consumerSessionId, CancellationToken cancellationToken)
        {
            if (_outstanding.TryGetValue(consumerSessionId, out ProviderPublicationData? existing))
            {
                return Task.FromResult(ReadProviderPublicationResult.Success(existing));
            }

            if (!_publications.TryGetValue(messageId, out ProviderPublicationData? publication))
            {
                return Task.FromResult(ReadProviderPublicationResult.NoPublication());
            }

            _outstanding[consumerSessionId] = publication;
            return Task.FromResult(ReadProviderPublicationResult.Success(publication));
        }

        public Task<ProviderPublicationRepositoryResult> MarkRecipientSkippedExpiredAsync(Guid messageId, Guid consumerSessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(ProviderPublicationRepositoryResult.Success());
        }

        public Task<ProviderPublicationRepositoryResult> MarkRecipientRemovedAsync(Guid messageId, Guid consumerSessionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RemoveProviderPublicationRecipientResult> RemoveOutstandingPublicationAsync(Guid consumerSessionId, CancellationToken cancellationToken)
        {
            if (!_outstanding.Remove(consumerSessionId, out ProviderPublicationData? publication))
            {
                return Task.FromResult(RemoveProviderPublicationRecipientResult.Success(null));
            }

            return Task.FromResult(RemoveProviderPublicationRecipientResult.Success(publication.MessageId));
        }

        public Task<ProviderPublicationRepositoryResult> MarkRecipientsSubscriptionDeletedAsync(Guid consumerSessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(ProviderPublicationRepositoryResult.Success());
        }
    }

    private sealed class CapturingSubscriptionTransport(ServiceBusMessage message) : ISubscriptionTransport
    {
        public Task<bool> TopicExistsAsync(string topicName, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task CreateSubscriptionAsync(
            string topicName,
            string subscriptionName,
            IReadOnlyList<string> consumerTopics,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DeleteSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<TransportPublication?> ReadPublicationAsync(
            Guid sessionId,
            string topicName,
            string subscriptionName,
            CancellationToken cancellationToken)
        {
            Guid messageId = Guid.Parse((string)message.ApplicationProperties[PublicationRoutingProperties.PublicationId]);
            return Task.FromResult<TransportPublication?>(new TransportPublication(
                messageId,
                ["Topic-A"],
                message.ContentType,
                message.Body));
        }

        public Task RemovePublicationAsync(Guid sessionId, string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class NoOpChannelTopicTransport : IChannelTopicTransport
    {
        public Task CreateTopicAsync(string topicName, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<bool> TopicExistsAsync(string topicName, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task DeleteTopicAsync(string topicName, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
