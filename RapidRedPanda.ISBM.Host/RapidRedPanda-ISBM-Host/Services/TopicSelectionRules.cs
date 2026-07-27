namespace RapidRedPanda.ISBM.Host.Services;

public static class TopicSelectionRules
{
    private const char TopicKeyDelimiter = '|';

    public static IReadOnlyList<string> DeduplicateTopics(IReadOnlyList<string> topics)
    {
        List<string> deduplicatedTopics = [];
        HashSet<string> seenTopics = new(StringComparer.Ordinal);

        foreach (string topic in topics)
        {
            if (seenTopics.Add(topic))
            {
                deduplicatedTopics.Add(topic);
            }
        }

        return deduplicatedTopics;
    }

    public static bool HasIntersection(IReadOnlyList<string> publicationTopics, IReadOnlyList<string> consumerTopics)
    {
        HashSet<string> consumerTopicSet = new(consumerTopics, StringComparer.Ordinal);
        return publicationTopics.Any(consumerTopicSet.Contains);
    }

    public static string BuildAzureSqlFilter(IReadOnlyList<string> consumerTopics)
    {
        if (consumerTopics.Count == 0)
        {
            throw new ArgumentException("At least one consumer topic is required.", nameof(consumerTopics));
        }

        IReadOnlyList<string> deduplicatedTopics = DeduplicateTopics(consumerTopics);
        List<string> predicates = [];

        foreach (string topic in deduplicatedTopics)
        {
            predicates.Add($"{PublicationRoutingProperties.TopicKeySet} LIKE '%{TopicKeyDelimiter}{ToTopicKey(topic)}{TopicKeyDelimiter}%'");
        }

        string filter = string.Join(" OR ", predicates);
        if (filter.Length > PublicationRoutingProperties.MaxSqlFilterLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(consumerTopics),
                $"The generated Azure Service Bus SQL filter is {filter.Length} characters, which exceeds the {PublicationRoutingProperties.MaxSqlFilterLength}-character limit.");
        }

        return filter;
    }

    public static string BuildTopicKeySet(IReadOnlyList<string> publicationTopics)
    {
        IReadOnlyList<string> deduplicatedTopics = DeduplicateTopics(publicationTopics);
        return $"{TopicKeyDelimiter}{string.Join(TopicKeyDelimiter, deduplicatedTopics.Select(ToTopicKey))}{TopicKeyDelimiter}";
    }

    public static string ToTopicKey(string topic)
    {
        return Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(topic));
    }
}
