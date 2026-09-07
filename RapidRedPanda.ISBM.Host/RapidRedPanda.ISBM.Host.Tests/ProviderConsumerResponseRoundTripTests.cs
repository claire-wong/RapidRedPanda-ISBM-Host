using Microsoft.Extensions.Logging.Abstractions;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Messaging;
using RapidRedPanda.ISBM.Host.Services;
using System.Text.Json;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class ProviderConsumerResponseRoundTripTests
{
    [Fact]
    public async Task ProviderPostResponse_CanBeReadByConsumerWithSameBinaryPayload()
    {
        Guid channelId = Guid.NewGuid();
        Guid providerSessionId = Guid.NewGuid();
        Guid consumerSessionId = Guid.NewGuid();
        Guid requestMessageId = Guid.NewGuid();
        FakeChannelRepository channelRepository = new(channelId);
        InMemoryRequestResponseRepository responseRepository = new(consumerSessionId);
        ProviderRequestSessionService providerService = new(
            channelRepository,
            new FakeProviderRequestSessionRepository(providerSessionId, channelId),
            new UnusedConsumerRequestRepository(),
            responseRepository,
            new UnusedSubscriptionTransport(),
            new NoOpChannelTopicTransport(),
            NullLogger<ProviderRequestSessionService>.Instance);

        InternalMessageContent responseContent = InternalMessageContent.FromBinary(
            BinaryData.FromBytes([0, 1, 2, 3, 4]),
            "application/octet-stream",
            "base64");

        PostResponseResult postResult = await providerService.PostResponseAsync(
            providerSessionId.ToString(),
            requestMessageId.ToString(),
            responseContent,
            null,
            CancellationToken.None);

        Assert.True(postResult.Succeeded);
        Assert.True(postResult.ResponseCreated);
        Assert.NotNull(responseRepository.InsertedResponse);
        Assert.Equal("Binary", responseRepository.InsertedResponse.MessageContent.PayloadKind);
        Assert.Equal("base64", responseRepository.InsertedResponse.MessageContent.ContentEncoding);
        Assert.Equal([0, 1, 2, 3, 4], responseRepository.InsertedResponse.MessageContent.Payload);
        Assert.Contains("\"contentEncoding\":\"base64\"", responseRepository.InsertedResponse.MessageContent.LegacyMessageContent, StringComparison.Ordinal);

        ConsumerRequestSessionService consumerService = new(
            channelRepository,
            new FakeConsumerRequestSessionRepository(consumerSessionId, channelId),
            new UnusedConsumerRequestRepository(),
            responseRepository,
            new UnusedPublicationTransport(),
            NullLogger<ConsumerRequestSessionService>.Instance);

        ReadResponseResult readResult = await consumerService.ReadResponseAsync(
            consumerSessionId.ToString(),
            requestMessageId.ToString(),
            null,
            CancellationToken.None);

        Assert.True(readResult.Succeeded);
        Assert.True(readResult.HasResponse);
        Assert.NotNull(readResult.Response);
        Assert.Equal(postResult.MessageId, readResult.Response.MessageId);
        Assert.Equal(requestMessageId, readResult.Response.RequestMessageId);
        Assert.Equal(InternalMessageContentKind.Binary, readResult.Response.MessageContent.Kind);
        Assert.Equal("application/octet-stream", readResult.Response.MessageContent.MediaType);
        Assert.Equal("base64", readResult.Response.MessageContent.ContentEncoding);
        Assert.Equal([0, 1, 2, 3, 4], readResult.Response.MessageContent.Payload.ToArray());

        JsonElement rest = RestMessageContentMapper.ToRestJsonElement(readResult.Response.MessageContent);
        Assert.Equal("AAECAwQ=", rest.GetProperty("content").GetString());
    }

    private sealed class InMemoryRequestResponseRepository(Guid consumerSessionId) : IRequestResponseRepository
    {
        public RequestResponseCreateData? InsertedResponse { get; private set; }

        public Task<ActiveRequestMatchResult> FindActiveRequestAsync(Guid requestMessageId, Guid channelId, CancellationToken cancellationToken)
        {
            return Task.FromResult(ActiveRequestMatchResult.Success(consumerSessionId));
        }

        public Task<RequestResponseRepositoryResult> InsertResponseAsync(RequestResponseCreateData response, CancellationToken cancellationToken)
        {
            InsertedResponse = response;
            return Task.FromResult(RequestResponseRepositoryResult.Success());
        }

        public Task<ReadRequestResponseResult> ReadResponseAsync(Guid requestedConsumerSessionId, Guid requestMessageId, CancellationToken cancellationToken)
        {
            if (InsertedResponse is null || requestedConsumerSessionId != consumerSessionId)
            {
                return Task.FromResult(ReadRequestResponseResult.NoResponse());
            }

            return Task.FromResult(ReadRequestResponseResult.Success(new RequestResponseData(
                1,
                InsertedResponse.MessageId,
                requestMessageId,
                consumerSessionId,
                InsertedResponse.ChannelId,
                InsertedResponse.MessageContent)));
        }

        public Task<RequestResponseRepositoryResult> RemoveOutstandingResponseAsync(Guid consumerRequestSessionId, Guid requestMessageId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeChannelRepository(Guid channelId) : IChannelRepository
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

        public Task<GetChannelTopicBindingResult> GetChannelTopicBindingByIdAsync(Guid channelId, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetChannelTopicBindingResult.Failure(GetChannelTopicBindingFailureKind.NotFound, "missing"));
        }

        public Task<GetChannelForSubscriptionResult> GetChannelForConsumerPublicationSubscriptionAsync(string channelUri, ChannelCredentials? credentials, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetChannelForSubscriptionResult.Failure(GetChannelForSubscriptionFailureKind.NotFound, "missing"));
        }

        public Task<GetChannelForSubscriptionResult> GetChannelForProviderPublicationSessionAsync(string channelUri, ChannelCredentials? credentials, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetChannelForSubscriptionResult.Failure(GetChannelForSubscriptionFailureKind.NotFound, "missing"));
        }

        public Task<ValidateChannelCredentialsResult> ValidateChannelCredentialsAsync(Guid requestedChannelId, string? expectedAuthenticatedTokenUsername, ChannelCredentials? credentials, CancellationToken cancellationToken)
        {
            return Task.FromResult(requestedChannelId == channelId
                ? ValidateChannelCredentialsResult.Success()
                : ValidateChannelCredentialsResult.Failure(ValidateChannelCredentialsFailureKind.NotFound, "missing"));
        }

        public Task<CreateChannelResult> CreateChannelAsync(ChannelCreateRequestData request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DeleteChannelResult> DeleteChannelAsync(string channelUri, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SecurityTokenMutationResult> AddSecurityTokensAsync(string channelUri, IReadOnlyList<ChannelCreateToken> tokens, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SecurityTokenMutationResult> RemoveSecurityTokensAsync(string channelUri, IReadOnlyList<ChannelCreateToken> tokens, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeProviderRequestSessionRepository(Guid sessionId, Guid channelId) : IProviderRequestSessionRepository
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
                "isbm-request-topic",
                $"isbm-prs-{sessionId:N}",
                ["Topic-A"],
                null,
                DateTime.UtcNow,
                null)));
        }

        public Task<CloseProviderRequestSessionRepositoryResult> CloseProviderRequestSessionAsync(Guid sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
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

    private sealed class UnusedConsumerRequestRepository : IConsumerRequestRepository
    {
        public Task<ConsumerRequestRepositoryResult> InsertPostedRequestAsync(ConsumerRequestCreateData request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ConsumerRequestRepositoryResult> ExpireRequestAsync(Guid consumerSessionId, Guid channelId, Guid messageId, DateTime expiredUtc, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RequestExpirationStateResult> IsRequestExpiredAsync(Guid messageId, Guid providerSessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedSubscriptionTransport : ISubscriptionTransport
    {
        public Task<bool> TopicExistsAsync(string topicName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CreateSubscriptionAsync(string topicName, string subscriptionName, IReadOnlyList<string> consumerTopics, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TransportPublication?> ReadPublicationAsync(Guid sessionId, string topicName, string subscriptionName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RemovePublicationAsync(Guid sessionId, string topicName, string subscriptionName, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedPublicationTransport : IPublicationTransport
    {
        public Task SendPublicationAsync(string topicName, Azure.Messaging.ServiceBus.ServiceBusMessage message, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoOpChannelTopicTransport : IChannelTopicTransport
    {
        public Task CreateTopicAsync(string topicName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> TopicExistsAsync(string topicName, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task DeleteTopicAsync(string topicName, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
