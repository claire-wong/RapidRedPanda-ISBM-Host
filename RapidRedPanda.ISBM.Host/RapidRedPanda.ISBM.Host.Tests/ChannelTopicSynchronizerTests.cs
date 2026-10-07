using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Services;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class ChannelTopicSynchronizerTests
{
    [Fact]
    public async Task SynchronizeOnce_MissingTopic_RecreatesAzureTopic()
    {
        FakeChannelRepository repository = new(
            new ChannelData(Guid.NewGuid(), "/channel/a", "Publication", null, "topic-a"));
        FakeChannelTopicTransport transport = new(existingTopics: []);
        ChannelTopicSynchronizer synchronizer = CreateSynchronizer(repository, transport);

        await synchronizer.SynchronizeOnceAsync(CancellationToken.None);

        Assert.Equal("topic-a", Assert.Single(transport.CreatedTopics));
    }

    [Fact]
    public async Task SynchronizeOnce_ExistingTopic_DoesNotRecreateAzureTopic()
    {
        FakeChannelRepository repository = new(
            new ChannelData(Guid.NewGuid(), "/channel/a", "Publication", null, "topic-a"));
        FakeChannelTopicTransport transport = new(existingTopics: ["topic-a"]);
        ChannelTopicSynchronizer synchronizer = CreateSynchronizer(repository, transport);

        await synchronizer.SynchronizeOnceAsync(CancellationToken.None);

        Assert.Empty(transport.CreatedTopics);
    }

    [Fact]
    public void ChannelTopicSynchronizationOptions_ValidateBounds()
    {
        Assert.True(ChannelTopicSynchronizationOptions.IsValid(new ChannelTopicSynchronizationOptions { IntervalSeconds = 30 }));
        Assert.False(ChannelTopicSynchronizationOptions.IsValid(new ChannelTopicSynchronizationOptions { IntervalSeconds = 29 }));
    }

    private static ChannelTopicSynchronizer CreateSynchronizer(
        FakeChannelRepository repository,
        FakeChannelTopicTransport transport)
    {
        ServiceCollection services = new();
        services.AddSingleton<IChannelRepository>(repository);
        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        return new ChannelTopicSynchronizer(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            transport,
            Options.Create(new ChannelTopicSynchronizationOptions()),
            NullLogger<ChannelTopicSynchronizer>.Instance);
    }

    private sealed class FakeChannelRepository : IChannelRepository
    {
        private readonly IReadOnlyList<ChannelData> _channels;

        public FakeChannelRepository(params ChannelData[] channels)
        {
            _channels = channels;
        }

        public Task<IReadOnlyList<ChannelData>> GetChannelsAsync(ChannelCredentials? credentials, CancellationToken cancellationToken)
        {
            return Task.FromResult(_channels);
        }

        public Task<GetChannelResult> GetChannelAsync(string channelUri, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<GetChannelTopicBindingResult> GetChannelTopicBindingAsync(string channelUri, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<GetChannelTopicBindingResult> GetChannelTopicBindingByIdAsync(Guid channelId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<GetChannelForSubscriptionResult> GetChannelForConsumerPublicationSubscriptionAsync(
            string channelUri,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<GetChannelForSubscriptionResult> GetChannelForProviderPublicationSessionAsync(
            string channelUri,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
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

    private sealed class FakeChannelTopicTransport : IChannelTopicTransport
    {
        private readonly HashSet<string> _existingTopics;

        public FakeChannelTopicTransport(IEnumerable<string> existingTopics)
        {
            _existingTopics = existingTopics.ToHashSet(StringComparer.Ordinal);
        }

        public List<string> CreatedTopics { get; } = [];

        public Task CreateTopicAsync(string topicName, CancellationToken cancellationToken)
        {
            CreatedTopics.Add(topicName);
            _existingTopics.Add(topicName);
            return Task.CompletedTask;
        }

        public Task<bool> TopicExistsAsync(string topicName, CancellationToken cancellationToken)
        {
            return Task.FromResult(_existingTopics.Contains(topicName));
        }

        public Task DeleteTopicAsync(string topicName, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
