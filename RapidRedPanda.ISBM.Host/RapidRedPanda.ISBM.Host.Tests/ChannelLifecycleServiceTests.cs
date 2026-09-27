using Azure;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Services;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class ChannelLifecycleServiceTests
{
    [Fact]
    public async Task CreateChannel_SuccessfulCreate_ProvisionsOneAzureTopicAndStoresExactName()
    {
        FakeChannelRepository repository = new();
        FakeChannelTopicTransport transport = new();
        ChannelLifecycleService service = CreateService(repository, transport);

        CreateChannelResult result = await service.CreateChannelAsync(
            "/channel/a",
            "Publication",
            null,
            [],
            CancellationToken.None);

        Assert.True(result.Succeeded);
        ChannelCreateRequestData request = Assert.Single(repository.CreateRequests);
        Assert.Equal(ChannelTransportNames.TopicNameForChannel(request.ChannelId), request.AzureTopicName);
        Assert.Equal(request.AzureTopicName, Assert.Single(transport.CreatedTopics));
        Assert.Equal(request.AzureTopicName, result.Channel!.AzureTopicName);
    }

    [Fact]
    public async Task CreateChannel_AzureCreateFailure_PreventsSqlInsert()
    {
        FakeChannelRepository repository = new();
        FakeChannelTopicTransport transport = new()
        {
            CreateException = new RequestFailedException(503, "unavailable")
        };

        CreateChannelResult result = await CreateService(repository, transport).CreateChannelAsync(
            "/channel/a",
            "Publication",
            null,
            [],
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(repository.CreateRequests);
        Assert.Empty(transport.DeletedTopics);
    }

    [Fact]
    public async Task CreateChannel_SqlFailure_TriggersAzureTopicCleanup()
    {
        FakeChannelRepository repository = new()
        {
            CreateResult = CreateChannelResult.Failure(CreateChannelFailureKind.DatabaseFailure, "failed")
        };
        FakeChannelTopicTransport transport = new();

        CreateChannelResult result = await CreateService(repository, transport).CreateChannelAsync(
            "/channel/a",
            "Publication",
            null,
            [],
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Single(repository.CreateRequests);
        Assert.Equal(transport.CreatedTopics, transport.DeletedTopics);
    }

    [Fact]
    public async Task DeleteChannel_DeletesExactStoredAzureTopicBeforeSqlDelete()
    {
        FakeChannelRepository repository = new()
        {
            BindingResult = GetChannelTopicBindingResult.Success(new ChannelTopicBindingData(
                Guid.NewGuid(),
                "/channel/a",
                "Publication",
                "stored-topic"))
        };
        FakeChannelTopicTransport transport = new();

        DeleteChannelResult result = await CreateService(repository, transport).DeleteChannelAsync("/channel/a", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("stored-topic", Assert.Single(transport.DeletedTopics));
        Assert.Equal("/channel/a", Assert.Single(repository.DeleteRequests));
    }

    [Fact]
    public async Task DeleteChannel_AzureDeletionFailure_DoesNotDeleteSqlChannel()
    {
        FakeChannelRepository repository = new()
        {
            BindingResult = GetChannelTopicBindingResult.Success(new ChannelTopicBindingData(
                Guid.NewGuid(),
                "/channel/a",
                "Publication",
                "stored-topic"))
        };
        FakeChannelTopicTransport transport = new()
        {
            DeleteException = new RequestFailedException(503, "unavailable")
        };

        DeleteChannelResult result = await CreateService(repository, transport).DeleteChannelAsync("/channel/a", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(repository.DeleteRequests);
    }

    [Fact]
    public async Task DeleteChannel_MissingAzureTopic_IsTreatedAsAlreadyDeleted()
    {
        FakeChannelRepository repository = new()
        {
            BindingResult = GetChannelTopicBindingResult.Success(new ChannelTopicBindingData(
                Guid.NewGuid(),
                "/channel/a",
                "Publication",
                "stored-topic"))
        };
        FakeChannelTopicTransport transport = new()
        {
            TreatDeleteAsMissing = true
        };

        DeleteChannelResult result = await CreateService(repository, transport).DeleteChannelAsync("/channel/a", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("/channel/a", Assert.Single(repository.DeleteRequests));
    }

    private static ChannelLifecycleService CreateService(
        FakeChannelRepository repository,
        FakeChannelTopicTransport transport)
    {
        return new ChannelLifecycleService(
            repository,
            transport,
            NullLogger<ChannelLifecycleService>.Instance);
    }

    private sealed class FakeChannelRepository : IChannelRepository
    {
        public List<ChannelCreateRequestData> CreateRequests { get; } = [];
        public List<string> DeleteRequests { get; } = [];
        public CreateChannelResult? CreateResult { get; init; }
        public GetChannelTopicBindingResult BindingResult { get; init; } =
            GetChannelTopicBindingResult.Failure(GetChannelTopicBindingFailureKind.NotFound, "missing");

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
            return Task.FromResult(BindingResult);
        }

        public Task<GetChannelTopicBindingResult> GetChannelTopicBindingByIdAsync(Guid channelId, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetChannelTopicBindingResult.Failure(GetChannelTopicBindingFailureKind.NotFound, "missing"));
        }

        public Task<GetChannelForSubscriptionResult> GetChannelForConsumerPublicationSubscriptionAsync(
            string channelUri,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(GetChannelForSubscriptionResult.Failure(GetChannelForSubscriptionFailureKind.NotFound, "missing"));
        }

        public Task<GetChannelForSubscriptionResult> GetChannelForProviderPublicationSessionAsync(
            string channelUri,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(GetChannelForSubscriptionResult.Failure(GetChannelForSubscriptionFailureKind.NotFound, "missing"));
        }

        public Task<CreateChannelResult> CreateChannelAsync(ChannelCreateRequestData request, CancellationToken cancellationToken)
        {
            CreateRequests.Add(request);
            return Task.FromResult(CreateResult ?? CreateChannelResult.Success(new ChannelData(
                request.ChannelId,
                request.ChannelUri,
                request.ChannelType,
                request.Description,
                request.AzureTopicName)));
        }

        public Task<DeleteChannelResult> DeleteChannelAsync(string channelUri, CancellationToken cancellationToken)
        {
            DeleteRequests.Add(channelUri);
            return Task.FromResult(DeleteChannelResult.Success());
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

    private sealed class FakeChannelTopicTransport : IChannelTopicTransport
    {
        public List<string> CreatedTopics { get; } = [];
        public List<string> DeletedTopics { get; } = [];
        public RequestFailedException? CreateException { get; init; }
        public RequestFailedException? DeleteException { get; init; }
        public bool TreatDeleteAsMissing { get; init; }

        public Task CreateTopicAsync(string topicName, CancellationToken cancellationToken)
        {
            if (CreateException is not null)
            {
                throw CreateException;
            }

            CreatedTopics.Add(topicName);
            return Task.CompletedTask;
        }

        public Task<bool> TopicExistsAsync(string topicName, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task DeleteTopicAsync(string topicName, CancellationToken cancellationToken)
        {
            if (DeleteException is not null)
            {
                throw DeleteException;
            }

            DeletedTopics.Add(topicName);
            return Task.CompletedTask;
        }
    }
}
