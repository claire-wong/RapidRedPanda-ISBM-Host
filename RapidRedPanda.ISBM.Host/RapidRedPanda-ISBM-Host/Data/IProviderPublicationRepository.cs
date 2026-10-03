namespace RapidRedPanda.ISBM.Host.Data;

public interface IProviderPublicationRepository
{
    Task<ProviderPublicationRepositoryResult> InsertPublishedPublicationAsync(
        ProviderPublicationCreateData publication,
        CancellationToken cancellationToken);

    Task<ProviderPublicationRepositoryResult> ExpirePublicationAsync(
        Guid providerSessionId,
        Guid channelId,
        Guid messageId,
        DateTime expiredUtc,
        CancellationToken cancellationToken);

    Task<PublicationExpirationStateResult> IsPublicationExpiredAsync(
        Guid messageId,
        Guid consumerSessionId,
        CancellationToken cancellationToken);

    Task<ProviderPublicationRepositoryResult> MarkRecipientOutstandingAsync(
        Guid messageId,
        Guid consumerSessionId,
        CancellationToken cancellationToken);

    Task<ProviderPublicationRepositoryResult> MarkRecipientSkippedExpiredAsync(
        Guid messageId,
        Guid consumerSessionId,
        CancellationToken cancellationToken);

    Task<ProviderPublicationRepositoryResult> MarkRecipientRemovedAsync(
        Guid messageId,
        Guid consumerSessionId,
        CancellationToken cancellationToken);

    Task<ProviderPublicationRepositoryResult> MarkRecipientsSubscriptionDeletedAsync(
        Guid consumerSessionId,
        CancellationToken cancellationToken);

    Task<ReadProviderPublicationResult> GetOutstandingPublicationAsync(
        Guid consumerSessionId,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    Task<ReadProviderPublicationResult> TryMarkPublicationOutstandingAsync(
        Guid messageId,
        Guid consumerSessionId,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    Task<RemoveProviderPublicationRecipientResult> RemoveOutstandingPublicationAsync(
        Guid consumerSessionId,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
