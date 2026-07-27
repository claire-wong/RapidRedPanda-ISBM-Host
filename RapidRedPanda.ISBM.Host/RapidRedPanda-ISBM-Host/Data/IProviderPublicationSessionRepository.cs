namespace RapidRedPanda.ISBM.Host.Data;

public interface IProviderPublicationSessionRepository
{
    Task<InsertProviderPublicationSessionResult> InsertProviderPublicationSessionAsync(
        ProviderPublicationSessionCreateData session,
        CancellationToken cancellationToken);

    Task<GetProviderPublicationSessionResult> GetProviderPublicationSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<CloseProviderPublicationSessionResult> CloseProviderPublicationSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken);
}
