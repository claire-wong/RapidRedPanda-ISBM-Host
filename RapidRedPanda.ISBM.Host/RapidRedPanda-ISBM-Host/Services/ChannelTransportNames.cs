namespace RapidRedPanda.ISBM.Host.Services;

public static class ChannelTransportNames
{
    public static string TopicNameForChannel(Guid channelId)
    {
        return $"isbm-ch-{channelId:N}";
    }

    public static string SubscriptionNameForSession(Guid sessionId)
    {
        return $"isbm-css-{sessionId:N}";
    }
}
