namespace RapidRedPanda.ISBM.Host.Services;

public static class PublicationRoutingProperties
{
    public const int MaxPublicationTopics = 32;
    public const int MaxSqlFilterLength = 1024;
    public const string PublicationId = "isbmPublicationId";
    public const string TopicCount = "isbmTopicCount";
    public const string TopicPropertyPrefix = "isbmTopic";
    public const string TopicKeySet = "isbmTopicKeys";

    public static string TopicPropertyName(int index)
    {
        if (index < 0 || index >= MaxPublicationTopics)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Topic index must be between 0 and {MaxPublicationTopics - 1}.");
        }

        return $"{TopicPropertyPrefix}{index}";
    }
}
