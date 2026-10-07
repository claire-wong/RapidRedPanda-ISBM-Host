namespace RapidRedPanda.ISBM.Host.Services;

public sealed class ChannelTopicSynchronizationOptions
{
    public const string SectionName = "ChannelTopicSynchronization";

    public bool Enabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = 300;

    public static bool IsValid(ChannelTopicSynchronizationOptions options)
    {
        return options.IntervalSeconds >= 30;
    }
}
