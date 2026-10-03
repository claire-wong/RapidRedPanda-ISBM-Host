using Microsoft.Extensions.Logging.Abstractions;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Messaging;
using RapidRedPanda.ISBM.Host.Services;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class PhaseThreeDurableRequestLifecycleTests
{
    [Fact]
    public async Task ReadRequest_DurableOutstandingStateSurvivesServiceAndTransportRecreation()
    {
        Guid providerSessionId = Guid.NewGuid();
        Guid channelId = Guid.NewGuid();
        Guid requestMessageId = Guid.NewGuid();
        Guid consumerSessionId = Guid.NewGuid();
        InternalMessageContent content = InternalMessageContent.FromString("restart-safe", "text/plain");
        DurableRequestRepository requestRepository = new(
            requestMessageId,
            consumerSessionId,
            channelId,
            "Topic-A",
            SqlMessageContentAdapter.ToSql(content));

        ProviderRequestSessionService firstService = CreateService(
            providerSessionId,
            channelId,
            requestRepository,
            new BrokerRequestTransport(requestMessageId, content));

        ReadRequestResult firstRead = await firstService.ReadRequestAsync(providerSessionId.ToString("D"), CancellationToken.None);

        Assert.True(firstRead.Succeeded);
        Assert.True(firstRead.HasRequest);
        Assert.Equal(requestMessageId, firstRead.Request?.MessageId);
        Assert.True(requestRepository.HasOutstanding(providerSessionId));

        ProviderRequestSessionService recreatedService = CreateService(
            providerSessionId,
            channelId,
            requestRepository,
            new EmptyRequestTransport());

        ReadRequestResult secondRead = await recreatedService.ReadRequestAsync(providerSessionId.ToString("D"), CancellationToken.None);

        Assert.True(secondRead.Succeeded);
        Assert.True(secondRead.HasRequest);
        Assert.Equal(requestMessageId, secondRead.Request?.MessageId);
        Assert.Equal(["Topic-A"], secondRead.Request?.Topics);
        Assert.Equal(InternalMessageContentKind.String, secondRead.Request?.MessageContent.Kind);
        Assert.Equal("restart-safe", secondRead.Request?.MessageContent.Payload.ToString());
    }

    [Fact]
    public async Task RemoveRequest_RemovesDurableOutstandingStateWithoutActiveBrokerLock()
    {
        Guid providerSessionId = Guid.NewGuid();
        Guid channelId = Guid.NewGuid();
        Guid requestMessageId = Guid.NewGuid();
        DurableRequestRepository requestRepository = new(
            requestMessageId,
            Guid.NewGuid(),
            channelId,
            "Topic-A",
            SqlMessageContentAdapter.ToSql(InternalMessageContent.FromJson(BinaryData.FromString("""{"kind":"request"}"""))));
        requestRepository.SeedOutstanding(providerSessionId);
        EmptyRequestTransport transport = new();

        RemoveRequestResult result = await CreateService(providerSessionId, channelId, requestRepository, transport)
            .RemoveRequestAsync(providerSessionId.ToString("D"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(requestRepository.HasOutstanding(providerSessionId));
        Assert.Equal(requestMessageId, requestRepository.RemovedRequestMessageId);
        Assert.False(transport.RemoveCalled);
    }

    private static ProviderRequestSessionService CreateService(
        Guid providerSessionId,
        Guid channelId,
        IConsumerRequestRepository requestRepository,
        ISubscriptionTransport subscriptionTransport)
    {
        return new ProviderRequestSessionService(
            new ThrowingChannelRepository(),
            new StaticProviderRequestSessionRepository(providerSessionId, channelId),
            requestRepository,
            new ThrowingRequestResponseRepository(),
            subscriptionTransport,
            new NoOpChannelTopicTransport(),
            NullLogger<ProviderRequestSessionService>.Instance);
    }

    private sealed class DurableRequestRepository(
        Guid requestMessageId,
        Guid consumerSessionId,
        Guid channelId,
        string topic,
        PersistedMessageContent content) : IConsumerRequestRepository
    {
        private readonly Dictionary<Guid, ConsumerRequestData> _outstanding = [];

        public Guid? RemovedRequestMessageId { get; private set; }

        public bool HasOutstanding(Guid providerSessionId)
        {
            return _outstanding.ContainsKey(providerSessionId);
        }

        public void SeedOutstanding(Guid providerSessionId)
        {
            _outstanding[providerSessionId] = Request();
        }

        public Task<ConsumerRequestRepositoryResult> InsertPostedRequestAsync(ConsumerRequestCreateData request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ConsumerRequestRepositoryResult> ExpireRequestAsync(Guid consumerSessionId, Guid channelId, Guid messageId, DateTime expiredUtc, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RequestExpirationStateResult> IsRequestExpiredAsync(Guid messageId, Guid providerSessionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ReadConsumerRequestResult> GetOutstandingRequestAsync(Guid providerSessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(
                _outstanding.TryGetValue(providerSessionId, out ConsumerRequestData? request)
                    ? ReadConsumerRequestResult.Success(request)
                    : ReadConsumerRequestResult.NoRequest());
        }

        public Task<ReadConsumerRequestResult> TryMarkRequestOutstandingAsync(
            Guid messageId,
            Guid providerSessionId,
            Guid requestedChannelId,
            IReadOnlyList<string> providerTopics,
            CancellationToken cancellationToken)
        {
            if (messageId != requestMessageId ||
                requestedChannelId != channelId ||
                !providerTopics.Contains(topic))
            {
                return Task.FromResult(ReadConsumerRequestResult.NoRequest());
            }

            ConsumerRequestData request = Request();
            _outstanding[providerSessionId] = request;
            return Task.FromResult(ReadConsumerRequestResult.Success(request));
        }

        public Task<RemoveConsumerRequestRecipientResult> RemoveOutstandingRequestAsync(Guid providerSessionId, CancellationToken cancellationToken)
        {
            if (_outstanding.Remove(providerSessionId, out ConsumerRequestData? request))
            {
                RemovedRequestMessageId = request.MessageId;
                return Task.FromResult(RemoveConsumerRequestRecipientResult.Success(request.MessageId));
            }

            return Task.FromResult(RemoveConsumerRequestRecipientResult.Success(null));
        }

        public Task<RequestResponseEligibilityResult> FindRespondableRequestAsync(Guid requestMessageId, Guid providerSessionId, Guid channelId, CancellationToken cancellationToken) => throw new NotSupportedException();

        private ConsumerRequestData Request()
        {
            return new ConsumerRequestData(requestMessageId, consumerSessionId, channelId, topic, content);
        }
    }

    private sealed class StaticProviderRequestSessionRepository(Guid sessionId, Guid channelId) : IProviderRequestSessionRepository
    {
        public Task<InsertProviderRequestSessionResult> InsertProviderRequestSessionAsync(ProviderRequestSessionCreateData session, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GetProviderRequestSessionResult> GetProviderRequestSessionAsync(Guid requestedSessionId, CancellationToken cancellationToken)
        {
            if (requestedSessionId != sessionId)
            {
                return Task.FromResult(GetProviderRequestSessionResult.Failure(GetProviderRequestSessionFailureKind.NotFound, "missing"));
            }

            return Task.FromResult(GetProviderRequestSessionResult.Success(new ProviderRequestSessionData(
                sessionId,
                channelId,
                SubscriptionSessionStates.Open,
                "request-topic",
                $"provider-{sessionId:N}",
                ["Topic-A"],
                null,
                DateTime.UtcNow,
                null)));
        }

        public Task<CloseProviderRequestSessionRepositoryResult> CloseProviderRequestSessionAsync(Guid sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class BrokerRequestTransport(Guid requestMessageId, InternalMessageContent content) : ISubscriptionTransport
    {
        private bool _delivered;

        public Task<bool> TopicExistsAsync(string topicName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CreateSubscriptionAsync(string topicName, string subscriptionName, IReadOnlyList<string> consumerTopics, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TransportPublication?> ReadPublicationAsync(Guid sessionId, string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            if (_delivered)
            {
                return Task.FromResult<TransportPublication?>(null);
            }

            _delivered = true;
            return Task.FromResult<TransportPublication?>(new TransportPublication(
                requestMessageId,
                ["Topic-A"],
                content.MediaType,
                BinaryData.FromBytes(BrokerMessageContentAdapter.CreateEnvelopeBody(requestMessageId, content, ["Topic-A"]))));
        }

        public Task RemovePublicationAsync(Guid sessionId, string topicName, string subscriptionName, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class EmptyRequestTransport : ISubscriptionTransport
    {
        public bool RemoveCalled { get; private set; }

        public Task<bool> TopicExistsAsync(string topicName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CreateSubscriptionAsync(string topicName, string subscriptionName, IReadOnlyList<string> consumerTopics, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TransportPublication?> ReadPublicationAsync(Guid sessionId, string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            return Task.FromResult<TransportPublication?>(null);
        }

        public Task RemovePublicationAsync(Guid sessionId, string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            RemoveCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingChannelRepository : IChannelRepository
    {
        public Task<IReadOnlyList<ChannelData>> GetChannelsAsync(ChannelCredentials? credentials, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GetChannelResult> GetChannelAsync(string channelUri, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GetChannelTopicBindingResult> GetChannelTopicBindingAsync(string channelUri, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GetChannelTopicBindingResult> GetChannelTopicBindingByIdAsync(Guid channelId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GetChannelForSubscriptionResult> GetChannelForConsumerPublicationSubscriptionAsync(string channelUri, ChannelCredentials? credentials, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GetChannelForSubscriptionResult> GetChannelForProviderPublicationSessionAsync(string channelUri, ChannelCredentials? credentials, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CreateChannelResult> CreateChannelAsync(ChannelCreateRequestData request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DeleteChannelResult> DeleteChannelAsync(string channelUri, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SecurityTokenMutationResult> AddSecurityTokensAsync(string channelUri, IReadOnlyList<ChannelCreateToken> tokens, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SecurityTokenMutationResult> RemoveSecurityTokensAsync(string channelUri, IReadOnlyList<ChannelCreateToken> tokens, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingRequestResponseRepository : IRequestResponseRepository
    {
        public Task<ActiveRequestMatchResult> FindActiveRequestAsync(Guid requestMessageId, Guid channelId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RequestResponseRepositoryResult> InsertResponseAsync(RequestResponseCreateData response, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReadRequestResponseResult> ReadResponseAsync(Guid consumerRequestSessionId, Guid requestMessageId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RequestResponseRepositoryResult> RemoveOutstandingResponseAsync(Guid consumerRequestSessionId, Guid requestMessageId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoOpChannelTopicTransport : IChannelTopicTransport
    {
        public Task CreateTopicAsync(string topicName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TopicExistsAsync(string topicName, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task DeleteTopicAsync(string topicName, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
