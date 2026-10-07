namespace RapidRedPanda.ISBM.Host.Data;

public interface ISubscriptionSessionRepository
{
    Task<InsertSubscriptionSessionResult> InsertConsumerSubscriptionSessionAsync(
        ConsumerSubscriptionSessionCreateData session,
        CancellationToken cancellationToken);

    Task<InsertSubscriptionSessionResult> InsertProviderRequestSessionAsync(
        ConsumerSubscriptionSessionCreateData session,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(InsertSubscriptionSessionResult.Failure(
            InsertSubscriptionSessionFailureKind.DatabaseFailure,
            "Provider request sessions are not supported by this repository."));
    }

    Task<GetSubscriptionSessionResult> GetSubscriptionSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConsumerSubscriptionSessionData>> GetConsumerSubscriptionSessionsForChannelAsync(
        Guid channelId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<ConsumerSubscriptionSessionData>>([]);
    }

    Task<CloseSubscriptionSessionResult> CloseSubscriptionSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken);
}
