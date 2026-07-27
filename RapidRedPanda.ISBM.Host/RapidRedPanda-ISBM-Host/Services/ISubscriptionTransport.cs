namespace RapidRedPanda.ISBM.Host.Services;

public interface ISubscriptionTransport
{
    Task<bool> TopicExistsAsync(
        string topicName,
        CancellationToken cancellationToken);

    Task CreateSubscriptionAsync(
        string topicName,
        string subscriptionName,
        IReadOnlyList<string> consumerTopics,
        CancellationToken cancellationToken);

    Task DeleteSubscriptionAsync(
        string topicName,
        string subscriptionName,
        CancellationToken cancellationToken);

    Task<TransportPublication?> ReadPublicationAsync(
        Guid sessionId,
        string topicName,
        string subscriptionName,
        CancellationToken cancellationToken);

    Task<TransportPublication?> GetOutstandingPublicationAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<TransportPublication?>(null);
    }

    Task RemovePublicationAsync(
        Guid sessionId,
        string topicName,
        string subscriptionName,
        CancellationToken cancellationToken);
}

public sealed record TransportPublication(
    Guid PublicationId,
    IReadOnlyList<string> Topics,
    string? ContentType,
    BinaryData Content);
