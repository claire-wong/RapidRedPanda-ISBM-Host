namespace RapidRedPanda.ISBM.Host.Data;

public interface IRequestResponseRepository
{
    Task<ActiveRequestMatchResult> FindActiveRequestAsync(
        Guid requestMessageId,
        Guid channelId,
        CancellationToken cancellationToken);

    Task<RequestResponseRepositoryResult> InsertResponseAsync(
        RequestResponseCreateData response,
        CancellationToken cancellationToken);

    Task<ReadRequestResponseResult> ReadResponseAsync(
        Guid consumerRequestSessionId,
        Guid requestMessageId,
        CancellationToken cancellationToken);

    Task<RequestResponseRepositoryResult> RemoveOutstandingResponseAsync(
        Guid consumerRequestSessionId,
        Guid requestMessageId,
        CancellationToken cancellationToken);
}
