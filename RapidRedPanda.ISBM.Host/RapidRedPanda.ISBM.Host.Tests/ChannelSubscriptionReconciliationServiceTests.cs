using Microsoft.Extensions.Logging.Abstractions;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Services;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class ChannelSubscriptionReconciliationServiceTests
{
    [Fact]
    public void TryGetSessionIdFromSubscriptionName_GeneratedName_ReturnsSessionId()
    {
        Guid sessionId = Guid.NewGuid();
        string subscriptionName = ChannelTransportNames.SubscriptionNameForSession(sessionId);

        bool parsed = ChannelTransportNames.TryGetSessionIdFromSubscriptionName(subscriptionName, out Guid parsedSessionId);

        Assert.True(parsed);
        Assert.Equal(sessionId, parsedSessionId);
    }

    [Theory]
    [InlineData("manual-subscription")]
    [InlineData("isbm-css-not-a-guid")]
    [InlineData("ISBM-CSS-0123456789abcdef0123456789abcdef")]
    [InlineData("isbm-css-0123456789abcdef0123456789abcdef-extra")]
    public void TryGetSessionIdFromSubscriptionName_NonGeneratedName_ReturnsFalse(string subscriptionName)
    {
        bool parsed = ChannelTransportNames.TryGetSessionIdFromSubscriptionName(subscriptionName, out Guid parsedSessionId);

        Assert.False(parsed);
        Assert.Equal(Guid.Empty, parsedSessionId);
    }

    [Fact]
    public async Task ReconcileChannelSubscriptions_DeletesGeneratedMissingAndClosedSubscriptionsOnly()
    {
        Guid channelId = Guid.NewGuid();
        Guid openSessionId = Guid.NewGuid();
        Guid closedSessionId = Guid.NewGuid();
        Guid orphanSessionId = Guid.NewGuid();
        string openSubscription = ChannelTransportNames.SubscriptionNameForSession(openSessionId);
        string closedSubscription = ChannelTransportNames.SubscriptionNameForSession(closedSessionId);
        string orphanSubscription = ChannelTransportNames.SubscriptionNameForSession(orphanSessionId);
        FakeSubscriptionTransport transport = new(
            openSubscription,
            closedSubscription,
            orphanSubscription,
            "manual-subscription");
        FakeSubscriptionSessionRepository repository = new(
            Session(channelId, openSessionId, openSubscription, SubscriptionSessionStates.Open),
            Session(channelId, closedSessionId, closedSubscription, SubscriptionSessionStates.Closed));

        ChannelSubscriptionReconciliationResult result = await CreateService(repository, transport)
            .ReconcileChannelSubscriptionsAsync(Channel(channelId), CancellationToken.None);

        Assert.Equal(4, result.InspectedSubscriptions);
        Assert.Equal(1, result.PreservedOpenSubscriptions);
        Assert.Equal(1, result.PreservedNonGeneratedSubscriptions);
        Assert.Equal(1, result.DeletedOrphanSubscriptions);
        Assert.Equal(1, result.DeletedClosedSubscriptions);
        Assert.Equal(0, result.DeleteFailures);
        Assert.Equal(
            [closedSubscription, orphanSubscription],
            transport.DeletedSubscriptions.Select(item => item.SubscriptionName).OrderBy(item => item, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task ReconcileChannelSubscriptions_DeleteFailure_DoesNotStopRemainingCleanup()
    {
        Guid channelId = Guid.NewGuid();
        string failingSubscription = ChannelTransportNames.SubscriptionNameForSession(Guid.NewGuid());
        string deletedSubscription = ChannelTransportNames.SubscriptionNameForSession(Guid.NewGuid());
        FakeSubscriptionTransport transport = new(failingSubscription, deletedSubscription)
        {
            DeleteFailureSubscriptionName = failingSubscription
        };

        ChannelSubscriptionReconciliationResult result = await CreateService(new FakeSubscriptionSessionRepository(), transport)
            .ReconcileChannelSubscriptionsAsync(Channel(channelId), CancellationToken.None);

        Assert.Equal(2, result.InspectedSubscriptions);
        Assert.Equal(1, result.DeletedOrphanSubscriptions);
        Assert.Equal(1, result.DeleteFailures);
        Assert.Equal(deletedSubscription, Assert.Single(transport.DeletedSubscriptions).SubscriptionName);
    }

    private static ChannelSubscriptionReconciliationService CreateService(
        FakeSubscriptionSessionRepository repository,
        FakeSubscriptionTransport transport)
    {
        return new ChannelSubscriptionReconciliationService(
            repository,
            transport,
            NullLogger<ChannelSubscriptionReconciliationService>.Instance);
    }

    private static ChannelSubscriptionData Channel(Guid channelId)
    {
        return new ChannelSubscriptionData(
            channelId,
            "/channel/a",
            "Publication",
            ChannelTransportNames.TopicNameForChannel(channelId),
            null);
    }

    private static ConsumerSubscriptionSessionData Session(
        Guid channelId,
        Guid sessionId,
        string subscriptionName,
        string sessionState)
    {
        return new ConsumerSubscriptionSessionData(
            sessionId,
            channelId,
            SubscriptionSessionTypes.ConsumerPublicationSubscription,
            sessionState,
            ChannelTransportNames.TopicNameForChannel(channelId),
            subscriptionName,
            ["Topic-A"],
            null,
            DateTime.UtcNow,
            string.Equals(sessionState, SubscriptionSessionStates.Closed, StringComparison.Ordinal) ? DateTime.UtcNow : null);
    }

    private sealed class FakeSubscriptionSessionRepository : ISubscriptionSessionRepository
    {
        private readonly IReadOnlyList<ConsumerSubscriptionSessionData> _sessions;

        public FakeSubscriptionSessionRepository(params ConsumerSubscriptionSessionData[] sessions)
        {
            _sessions = sessions;
        }

        public Task<InsertSubscriptionSessionResult> InsertConsumerSubscriptionSessionAsync(
            ConsumerSubscriptionSessionCreateData session,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<GetSubscriptionSessionResult> GetSubscriptionSessionAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<ConsumerSubscriptionSessionData>> GetConsumerSubscriptionSessionsForChannelAsync(
            Guid channelId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ConsumerSubscriptionSessionData>>(
                _sessions.Where(session => session.ChannelId == channelId).ToArray());
        }

        public Task<CloseSubscriptionSessionResult> CloseSubscriptionSessionAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeSubscriptionTransport : ISubscriptionTransport
    {
        private readonly IReadOnlyList<string> _subscriptionNames;

        public FakeSubscriptionTransport(params string[] subscriptionNames)
        {
            _subscriptionNames = subscriptionNames;
        }

        public string? DeleteFailureSubscriptionName { get; init; }
        public List<(string TopicName, string SubscriptionName)> DeletedSubscriptions { get; } = [];

        public Task<bool> TopicExistsAsync(string topicName, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
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
            if (string.Equals(subscriptionName, DeleteFailureSubscriptionName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("delete failed");
            }

            DeletedSubscriptions.Add((topicName, subscriptionName));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetSubscriptionNamesAsync(string topicName, CancellationToken cancellationToken)
        {
            return Task.FromResult(_subscriptionNames);
        }

        public Task<TransportPublication?> ReadPublicationAsync(
            Guid sessionId,
            string topicName,
            string subscriptionName,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task RemovePublicationAsync(
            Guid sessionId,
            string topicName,
            string subscriptionName,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
