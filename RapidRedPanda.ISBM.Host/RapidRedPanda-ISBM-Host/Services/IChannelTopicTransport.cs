namespace RapidRedPanda.ISBM.Host.Services;

public interface IChannelTopicTransport
{
    Task CreateTopicAsync(
        string topicName,
        CancellationToken cancellationToken);

    Task<bool> TopicExistsAsync(
        string topicName,
        CancellationToken cancellationToken);

    Task DeleteTopicAsync(
        string topicName,
        CancellationToken cancellationToken);
}
