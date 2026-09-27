using RapidRedPanda.ISBM.Host.Services;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class PublicationTopicRoutingTests
{
    [Fact]
    public void HasIntersection_OnePublicationTopicMatchesOneConsumerTopic()
    {
        Assert.True(TopicSelectionRules.HasIntersection(["Topic-A"], ["Topic-A"]));
    }

    [Fact]
    public void HasIntersection_NoPublicationTopicsMatch()
    {
        Assert.False(TopicSelectionRules.HasIntersection(["Topic-A"], ["Topic-B"]));
    }

    [Fact]
    public void HasIntersection_MultiplePublicationTopicsMatchOneConsumerTopic()
    {
        Assert.True(TopicSelectionRules.HasIntersection(["Topic-A", "Topic-B", "Topic-C"], ["Topic-B"]));
    }

    [Fact]
    public void DeduplicateTopics_OnePublicationTopicMatchesDuplicateConsumerEntries_UsesOneLogicalTopic()
    {
        IReadOnlyList<string> topics = TopicSelectionRules.DeduplicateTopics(["Topic-A", "Topic-A", "Topic-B"]);

        Assert.Equal(["Topic-A", "Topic-B"], topics);
    }

    [Fact]
    public void HasIntersection_MultiplePublicationTopicsMatchMultipleConsumerTopics()
    {
        Assert.True(TopicSelectionRules.HasIntersection(
            ["Topic-A", "Topic-B", "Topic-C"],
            ["Topic-B", "Topic-C", "Topic-D"]));
    }

    [Fact]
    public void PublicationMessageFactory_ThreeTopicPublicationCreatesOneAzureMessage()
    {
        PublicationMessageEnvelope envelope = PublicationMessageFactory.CreateMessage(
            BinaryData.FromString("payload"),
            ["Topic-A", "Topic-B", "Topic-C"]);

        Assert.NotEqual(Guid.Empty, envelope.PublicationId);
        Assert.Equal(3, envelope.Topics.Count);
        Assert.Equal(envelope.PublicationId.ToString("D"), envelope.Message.MessageId);
        Assert.Equal(envelope.PublicationId.ToString("D"), envelope.Message.ApplicationProperties[PublicationRoutingProperties.PublicationId]);
        Assert.Equal(3, envelope.Message.ApplicationProperties[PublicationRoutingProperties.TopicCount]);
        Assert.Equal("Topic-A", envelope.Message.ApplicationProperties[PublicationRoutingProperties.TopicPropertyName(0)]);
        Assert.Equal("Topic-B", envelope.Message.ApplicationProperties[PublicationRoutingProperties.TopicPropertyName(1)]);
        Assert.Equal("Topic-C", envelope.Message.ApplicationProperties[PublicationRoutingProperties.TopicPropertyName(2)]);
        Assert.Equal(
            "|546F7069632D41|546F7069632D42|546F7069632D43|",
            envelope.Message.ApplicationProperties[PublicationRoutingProperties.TopicKeySet]);
    }

    [Fact]
    public void PublicationMessageFactory_MultipleDuplicatePublicationTopicsRemainOneLogicalMessage()
    {
        PublicationMessageEnvelope envelope = PublicationMessageFactory.CreateMessage(
            BinaryData.FromString("payload"),
            ["Topic-A", "Topic-A", "Topic-B"]);

        Assert.Equal(["Topic-A", "Topic-B"], envelope.Topics);
        Assert.Equal(2, envelope.Message.ApplicationProperties[PublicationRoutingProperties.TopicCount]);
        Assert.False(envelope.Message.ApplicationProperties.ContainsKey(PublicationRoutingProperties.TopicPropertyName(2)));
    }

    [Fact]
    public void PublicationMessageFactory_RejectsTooManyPublicationTopicsRatherThanTruncating()
    {
        string[] topics = Enumerable.Range(0, PublicationRoutingProperties.MaxPublicationTopics + 1)
            .Select(index => $"Topic-{index}")
            .ToArray();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PublicationMessageFactory.CreateMessage(BinaryData.FromString("payload"), topics));
    }

    [Fact]
    public void BuildAzureSqlFilter_UsesCompactHostOwnedTopicKeyProperty()
    {
        string filter = TopicSelectionRules.BuildAzureSqlFilter(["Topic-A", "O'Hare"]);

        Assert.Contains("isbmTopicKeys LIKE '%|546F7069632D41|%'", filter);
        Assert.Contains("isbmTopicKeys LIKE '%|4F2748617265|%'", filter);
        Assert.True(filter.Length <= PublicationRoutingProperties.MaxSqlFilterLength);
        Assert.DoesNotContain("$Default", filter, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", filter, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildAzureSqlFilter_DeduplicatesConsumerTopicsWithoutChangingCaseSemantics()
    {
        string filter = TopicSelectionRules.BuildAzureSqlFilter(["Topic-A", "Topic-A", "topic-a"]);

        Assert.Contains("isbmTopicKeys LIKE '%|546F7069632D41|%'", filter);
        Assert.Contains("isbmTopicKeys LIKE '%|746F7069632D61|%'", filter);
        Assert.Equal(1, CountOccurrences(filter, "isbmTopicKeys LIKE '%|546F7069632D41|%'"));
    }

    [Fact]
    public void BuildAzureSqlFilter_ObservedTopicStaysUnderAzureSqlFilterLimit()
    {
        string filter = TopicSelectionRules.BuildAzureSqlFilter(["OIIE:S30:V1.1/CCOM-JSON:SyncMeasurements:V1.0"]);

        Assert.Contains("isbmTopicKeys LIKE '%|4F4949453A5333303A56312E312F43434F4D2D4A534F4E3A53796E634D6561737572656D656E74733A56312E30|%'", filter);
        Assert.True(filter.Length <= PublicationRoutingProperties.MaxSqlFilterLength);
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int startIndex = 0;

        while (true)
        {
            int index = text.IndexOf(value, startIndex, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            startIndex = index + value.Length;
        }
    }
}
