namespace RapidRedPanda.ISBM.Host.Data;

public interface IChannelRepository
{
    Task<IReadOnlyList<ChannelData>> GetChannelsAsync(ChannelCredentials? credentials, CancellationToken cancellationToken);

    Task<GetChannelResult> GetChannelAsync(string channelUri, CancellationToken cancellationToken);

    Task<GetChannelTopicBindingResult> GetChannelTopicBindingAsync(string channelUri, CancellationToken cancellationToken);

    Task<GetChannelTopicBindingResult> GetChannelTopicBindingByIdAsync(Guid channelId, CancellationToken cancellationToken);

    Task<GetChannelForSubscriptionResult> GetChannelForConsumerPublicationSubscriptionAsync(
        string channelUri,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken);

    Task<GetChannelForSubscriptionResult> GetChannelForProviderPublicationSessionAsync(
        string channelUri,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken);

    Task<GetChannelForSubscriptionResult> GetChannelForProviderRequestSessionAsync(
        string channelUri,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(GetChannelForSubscriptionResult.Failure(GetChannelForSubscriptionFailureKind.NotFound, "missing"));
    }

    Task<GetChannelForSubscriptionResult> GetChannelForConsumerRequestSessionAsync(
        string channelUri,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(GetChannelForSubscriptionResult.Failure(GetChannelForSubscriptionFailureKind.NotFound, "missing"));
    }

    Task<ValidateChannelCredentialsResult> ValidateChannelCredentialsAsync(
        Guid channelId,
        string? expectedAuthenticatedTokenUsername,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ValidateChannelCredentialsResult.Success());
    }

    Task<CreateChannelResult> CreateChannelAsync(ChannelCreateRequestData request, CancellationToken cancellationToken);

    Task<DeleteChannelResult> DeleteChannelAsync(string channelUri, CancellationToken cancellationToken);

    Task<SecurityTokenMutationResult> AddSecurityTokensAsync(string channelUri, IReadOnlyList<ChannelCreateToken> tokens, CancellationToken cancellationToken);

    Task<SecurityTokenMutationResult> RemoveSecurityTokensAsync(string channelUri, IReadOnlyList<ChannelCreateToken> tokens, CancellationToken cancellationToken);
}
