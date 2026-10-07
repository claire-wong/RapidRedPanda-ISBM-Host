using RapidRedPanda.ISBM.Host.Data;

namespace RapidRedPanda.ISBM.Host.Services;

public interface IChannelSubscriptionReconciliationService
{
    Task<ChannelSubscriptionReconciliationResult> ReconcileChannelSubscriptionsAsync(
        ChannelSubscriptionData channel,
        CancellationToken cancellationToken);
}

public sealed record ChannelSubscriptionReconciliationResult(
    int InspectedSubscriptions,
    int PreservedOpenSubscriptions,
    int PreservedNonGeneratedSubscriptions,
    int DeletedOrphanSubscriptions,
    int DeletedClosedSubscriptions,
    int DeleteFailures)
{
    public static ChannelSubscriptionReconciliationResult Empty()
    {
        return new ChannelSubscriptionReconciliationResult(0, 0, 0, 0, 0, 0);
    }
}

public sealed class ChannelSubscriptionReconciliationService : IChannelSubscriptionReconciliationService
{
    private readonly ISubscriptionSessionRepository _sessionRepository;
    private readonly ISubscriptionTransport _subscriptionTransport;
    private readonly ILogger<ChannelSubscriptionReconciliationService> _logger;

    public ChannelSubscriptionReconciliationService(
        ISubscriptionSessionRepository sessionRepository,
        ISubscriptionTransport subscriptionTransport,
        ILogger<ChannelSubscriptionReconciliationService> logger)
    {
        _sessionRepository = sessionRepository;
        _subscriptionTransport = subscriptionTransport;
        _logger = logger;
    }

    public async Task<ChannelSubscriptionReconciliationResult> ReconcileChannelSubscriptionsAsync(
        ChannelSubscriptionData channel,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> azureSubscriptionNames = await _subscriptionTransport.GetSubscriptionNamesAsync(
            channel.AzureTopicName,
            cancellationToken);
        IReadOnlyList<ConsumerSubscriptionSessionData> sqlSessions =
            await _sessionRepository.GetConsumerSubscriptionSessionsForChannelAsync(channel.ChannelId, cancellationToken);

        Dictionary<string, ConsumerSubscriptionSessionData> sqlSessionsBySubscriptionName = sqlSessions
            .GroupBy(session => session.AzureSubscriptionName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        int preservedOpen = 0;
        int preservedNonGenerated = 0;
        int deletedOrphan = 0;
        int deletedClosed = 0;
        int deleteFailures = 0;

        foreach (string subscriptionName in azureSubscriptionNames)
        {
            if (!ChannelTransportNames.TryGetSessionIdFromSubscriptionName(subscriptionName, out _))
            {
                preservedNonGenerated++;
                continue;
            }

            if (sqlSessionsBySubscriptionName.TryGetValue(subscriptionName, out ConsumerSubscriptionSessionData? session) &&
                string.Equals(session.SessionState, SubscriptionSessionStates.Open, StringComparison.Ordinal))
            {
                preservedOpen++;
                continue;
            }

            bool isClosedSqlSession = session is not null &&
                string.Equals(session.SessionState, SubscriptionSessionStates.Closed, StringComparison.Ordinal);

            try
            {
                await _subscriptionTransport.DeleteSubscriptionAsync(
                    channel.AzureTopicName,
                    subscriptionName,
                    cancellationToken);

                if (isClosedSqlSession)
                {
                    deletedClosed++;
                }
                else
                {
                    deletedOrphan++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                deleteFailures++;
                _logger.LogError(
                    exception,
                    "Azure Service Bus stale subscription cleanup failed. ChannelId: {ChannelId}. ChannelUri: {ChannelUri}. AzureTopicName: {AzureTopicName}. AzureSubscriptionName: {AzureSubscriptionName}.",
                    channel.ChannelId,
                    channel.ChannelUri,
                    channel.AzureTopicName,
                    subscriptionName);
            }
        }

        return new ChannelSubscriptionReconciliationResult(
            azureSubscriptionNames.Count,
            preservedOpen,
            preservedNonGenerated,
            deletedOrphan,
            deletedClosed,
            deleteFailures);
    }
}

public sealed class NoOpChannelSubscriptionReconciliationService : IChannelSubscriptionReconciliationService
{
    public Task<ChannelSubscriptionReconciliationResult> ReconcileChannelSubscriptionsAsync(
        ChannelSubscriptionData channel,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ChannelSubscriptionReconciliationResult.Empty());
    }
}
