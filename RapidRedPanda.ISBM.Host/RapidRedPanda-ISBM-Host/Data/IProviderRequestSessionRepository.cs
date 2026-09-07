namespace RapidRedPanda.ISBM.Host.Data;

public interface IProviderRequestSessionRepository
{
    Task<InsertProviderRequestSessionResult> InsertProviderRequestSessionAsync(
        ProviderRequestSessionCreateData session,
        CancellationToken cancellationToken);

    Task<GetProviderRequestSessionResult> GetProviderRequestSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<CloseProviderRequestSessionRepositoryResult> CloseProviderRequestSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken);
}
