namespace RapidRedPanda.ISBM.Host.Data;

public interface IConsumerRequestSessionRepository
{
    Task<InsertConsumerRequestSessionResult> InsertConsumerRequestSessionAsync(
        ConsumerRequestSessionCreateData session,
        CancellationToken cancellationToken);

    Task<GetConsumerRequestSessionResult> GetConsumerRequestSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<CloseConsumerRequestSessionResult> CloseConsumerRequestSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken);
}
