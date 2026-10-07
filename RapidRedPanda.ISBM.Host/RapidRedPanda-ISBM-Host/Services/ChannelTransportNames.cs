namespace RapidRedPanda.ISBM.Host.Services;

public static class ChannelTransportNames
{
    private const string SubscriptionPrefix = "isbm-css-";

    public static string TopicNameForChannel(Guid channelId)
    {
        return $"isbm-ch-{channelId:N}";
    }

    public static string SubscriptionNameForSession(Guid sessionId)
    {
        return $"{SubscriptionPrefix}{sessionId:N}";
    }

    public static bool TryGetSessionIdFromSubscriptionName(string subscriptionName, out Guid sessionId)
    {
        sessionId = Guid.Empty;

        if (!subscriptionName.StartsWith(SubscriptionPrefix, StringComparison.Ordinal) ||
            subscriptionName.Length != SubscriptionPrefix.Length + 32)
        {
            return false;
        }

        return Guid.TryParseExact(subscriptionName[SubscriptionPrefix.Length..], "N", out sessionId);
    }
}
