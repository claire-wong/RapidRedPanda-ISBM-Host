namespace RapidRedPanda.ISBM.Host.Data;

public interface IConsumerRequestRepository
{
    Task<ConsumerRequestRepositoryResult> InsertPostedRequestAsync(
        ConsumerRequestCreateData request,
        CancellationToken cancellationToken);

    Task<ConsumerRequestRepositoryResult> ExpireRequestAsync(
        Guid consumerSessionId,
        Guid channelId,
        Guid messageId,
        DateTime expiredUtc,
        CancellationToken cancellationToken);

    Task<RequestExpirationStateResult> IsRequestExpiredAsync(
        Guid messageId,
        Guid providerSessionId,
        CancellationToken cancellationToken);

    Task<ReadConsumerRequestResult> GetOutstandingRequestAsync(
        Guid providerSessionId,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    Task<ReadConsumerRequestResult> TryMarkRequestOutstandingAsync(
        Guid messageId,
        Guid providerSessionId,
        Guid channelId,
        IReadOnlyList<string> providerTopics,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    Task<RemoveConsumerRequestRecipientResult> RemoveOutstandingRequestAsync(
        Guid providerSessionId,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    Task<RequestResponseEligibilityResult> FindRespondableRequestAsync(
        Guid requestMessageId,
        Guid providerSessionId,
        Guid channelId,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
