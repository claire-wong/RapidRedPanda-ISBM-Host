using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Messaging;
using RapidRedPanda.ISBM.Host.Services;
using System.Text.Json;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class ProviderConsumerRequestRoundTripTests
{
    [Fact]
    public async Task ConsumerPostRequest_CanBeReadByProviderWithSameBinaryPayload()
    {
        Guid channelId = Guid.NewGuid();
        Guid consumerSessionId = Guid.NewGuid();
        Guid providerSessionId = Guid.NewGuid();
        const string azureTopicName = "isbm-request-topic";
        FakeChannelRepository channelRepository = new(channelId, azureTopicName);
        FakeConsumerRequestSessionRepository consumerSessionRepository = new(consumerSessionId, channelId);
        FakeSubscriptionSessionRepository providerSessionRepository = new(providerSessionId, channelId, azureTopicName);
        FakeConsumerRequestRepository requestRepository = new();
        CapturingPublicationTransport publicationTransport = new();
        ConsumerRequestSessionService consumerService = new(
            channelRepository,
            consumerSessionRepository,
            requestRepository,
            new UnusedRequestResponseRepository(),
            publicationTransport,
            NullLogger<ConsumerRequestSessionService>.Instance);

        InternalMessageContent requestContent = InternalMessageContent.FromBinary(
            BinaryData.FromBytes([0, 1, 2, 3, 4]),
            "application/octet-stream",
            "base64");

        PostRequestResult postResult = await consumerService.PostRequestAsync(
            consumerSessionId.ToString(),
            ["Topic-A"],
            requestContent,
            null,
            CancellationToken.None);

        Assert.True(postResult.Succeeded);
        ServiceBusMessage sentMessage = Assert.Single(publicationTransport.SentMessages);
        Assert.Equal("Request", sentMessage.ApplicationProperties["isbmMessageKind"]);
        Assert.Equal(consumerSessionId.ToString("D"), sentMessage.ApplicationProperties["isbmConsumerRequestSessionId"]);
        Assert.Equal("application/octet-stream", sentMessage.ContentType);

        ProviderRequestSessionService providerService = new(
            channelRepository,
            providerSessionRepository,
            requestRepository,
            new UnusedRequestResponseRepository(),
            new CapturingSubscriptionTransport(sentMessage),
            new NoOpChannelTopicTransport(),
            NullLogger<ProviderRequestSessionService>.Instance);

        ReadRequestResult readResult = await providerService.ReadRequestAsync(
            providerSessionId.ToString(),
            CancellationToken.None);

        Assert.True(readResult.Succeeded);
        Assert.True(readResult.HasRequest);
        Assert.NotNull(readResult.Request);
        Assert.Equal(postResult.MessageId, readResult.Request.MessageId);
        Assert.Equal(["Topic-A"], readResult.Request.Topics);
        Assert.Equal(InternalMessageContentKind.Binary, readResult.Request.MessageContent.Kind);
        Assert.Equal("application/octet-stream", readResult.Request.MessageContent.MediaType);
        Assert.Equal("base64", readResult.Request.MessageContent.ContentEncoding);
        Assert.Equal([0, 1, 2, 3, 4], readResult.Request.MessageContent.Payload.ToArray());

        JsonElement rest = RestMessageContentMapper.ToRestJsonElement(readResult.Request.MessageContent);
        Assert.Equal("AAECAwQ=", rest.GetProperty("content").GetString());
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
                "/request/channel",
                "Request",
                azureTopicName)));
        }

        public Task<GetChannelForSubscriptionResult> GetChannelForConsumerPublicationSubscriptionAsync(string channelUri, ChannelCredentials? credentials, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetChannelForSubscriptionResult.Failure(GetChannelForSubscriptionFailureKind.NotFound, "missing"));
        }

        public Task<GetChannelForSubscriptionResult> GetChannelForProviderPublicationSessionAsync(string channelUri, ChannelCredentials? credentials, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetChannelForSubscriptionResult.Failure(GetChannelForSubscriptionFailureKind.NotFound, "missing"));
        }

        public Task<CreateChannelResult> CreateChannelAsync(ChannelCreateRequestData request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DeleteChannelResult> DeleteChannelAsync(string channelUri, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SecurityTokenMutationResult> AddSecurityTokensAsync(string channelUri, IReadOnlyList<ChannelCreateToken> tokens, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SecurityTokenMutationResult> RemoveSecurityTokensAsync(string channelUri, IReadOnlyList<ChannelCreateToken> tokens, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeConsumerRequestSessionRepository(Guid sessionId, Guid channelId) : IConsumerRequestSessionRepository
    {
        public Task<InsertConsumerRequestSessionResult> InsertConsumerRequestSessionAsync(ConsumerRequestSessionCreateData session, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GetConsumerRequestSessionResult> GetConsumerRequestSessionAsync(Guid requestedSessionId, CancellationToken cancellationToken)
        {
            if (requestedSessionId != sessionId)
            {
                return Task.FromResult(GetConsumerRequestSessionResult.Failure(GetConsumerRequestSessionFailureKind.NotFound, "missing"));
            }

            return Task.FromResult(GetConsumerRequestSessionResult.Success(new ConsumerRequestSessionData(
                sessionId,
                channelId,
                ConsumerRequestSessionStates.Open,
                null,
                DateTime.UtcNow,
                null)));
        }

        public Task<CloseConsumerRequestSessionResult> CloseConsumerRequestSessionAsync(Guid sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeSubscriptionSessionRepository(Guid sessionId, Guid channelId, string azureTopicName) : ISubscriptionSessionRepository
    {
        public Task<InsertSubscriptionSessionResult> InsertConsumerSubscriptionSessionAsync(ConsumerSubscriptionSessionCreateData session, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GetSubscriptionSessionResult> GetSubscriptionSessionAsync(Guid requestedSessionId, CancellationToken cancellationToken)
        {
            if (requestedSessionId != sessionId)
            {
                return Task.FromResult(GetSubscriptionSessionResult.Failure(GetSubscriptionSessionFailureKind.NotFound, "missing"));
            }

            return Task.FromResult(GetSubscriptionSessionResult.Success(new ConsumerSubscriptionSessionData(
                sessionId,
                channelId,
                SubscriptionSessionTypes.ProviderRequestSubscription,
                SubscriptionSessionStates.Open,
                azureTopicName,
                $"isbm-prs-{sessionId:N}",
                ["Topic-A"],
                null,
                DateTime.UtcNow,
                null)));
        }

        public Task<CloseSubscriptionSessionResult> CloseSubscriptionSessionAsync(Guid sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeConsumerRequestRepository : IConsumerRequestRepository
    {
        public Task<ConsumerRequestRepositoryResult> InsertPostedRequestAsync(ConsumerRequestCreateData request, CancellationToken cancellationToken)
        {
            return Task.FromResult(ConsumerRequestRepositoryResult.Success());
        }

        public Task<ConsumerRequestRepositoryResult> ExpireRequestAsync(Guid consumerSessionId, Guid channelId, Guid messageId, DateTime expiredUtc, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RequestExpirationStateResult> IsRequestExpiredAsync(Guid messageId, Guid providerSessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(RequestExpirationStateResult.Success(false));
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

    private sealed class CapturingSubscriptionTransport(ServiceBusMessage message) : ISubscriptionTransport
    {
        public Task<bool> TopicExistsAsync(string topicName, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task CreateSubscriptionAsync(string topicName, string subscriptionName, IReadOnlyList<string> consumerTopics, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TransportPublication?> ReadPublicationAsync(Guid sessionId, string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            Guid messageId = Guid.Parse((string)message.ApplicationProperties[PublicationRoutingProperties.PublicationId]);
            return Task.FromResult<TransportPublication?>(new TransportPublication(
                messageId,
                ["Topic-A"],
                message.ContentType,
                message.Body));
        }

        public Task RemovePublicationAsync(Guid sessionId, string topicName, string subscriptionName, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoOpChannelTopicTransport : IChannelTopicTransport
    {
        public Task CreateTopicAsync(string topicName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> TopicExistsAsync(string topicName, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task DeleteTopicAsync(string topicName, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedRequestResponseRepository : IRequestResponseRepository
    {
        public Task<ActiveRequestMatchResult> FindActiveRequestAsync(Guid requestMessageId, Guid channelId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RequestResponseRepositoryResult> InsertResponseAsync(RequestResponseCreateData response, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ReadRequestResponseResult> ReadResponseAsync(Guid consumerRequestSessionId, Guid requestMessageId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RequestResponseRepositoryResult> RemoveOutstandingResponseAsync(Guid consumerRequestSessionId, Guid requestMessageId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
