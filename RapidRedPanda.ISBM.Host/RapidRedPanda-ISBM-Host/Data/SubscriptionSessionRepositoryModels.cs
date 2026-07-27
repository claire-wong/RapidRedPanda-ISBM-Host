namespace RapidRedPanda.ISBM.Host.Data;

public static class SubscriptionSessionTypes
{
    public const string ConsumerPublicationSubscription = "ConsumerPublicationSubscription";
    public const string ProviderRequestSubscription = "ProviderRequestSubscription";
}

public static class SubscriptionSessionStates
{
    public const string Open = "Open";
    public const string Closed = "Closed";
}

public sealed record ConsumerSubscriptionSessionCreateData(
    Guid SessionId,
    Guid ChannelId,
    string AzureTopicName,
    string AzureSubscriptionName,
    IReadOnlyList<string> RequestedTopics,
    string? AuthenticatedTokenUsername);

public sealed record ConsumerSubscriptionSessionData(
    Guid SessionId,
    Guid ChannelId,
    string SessionType,
    string SessionState,
    string AzureTopicName,
    string AzureSubscriptionName,
    IReadOnlyList<string> RequestedTopics,
    string? AuthenticatedTokenUsername,
    DateTime OpenedUtc,
    DateTime? ClosedUtc);

public enum InsertSubscriptionSessionFailureKind
{
    DatabaseFailure
}

public sealed record InsertSubscriptionSessionResult(bool Succeeded, InsertSubscriptionSessionFailureKind? FailureKind, string? ErrorMessage)
{
    public static InsertSubscriptionSessionResult Success()
    {
        return new InsertSubscriptionSessionResult(true, null, null);
    }

    public static InsertSubscriptionSessionResult Failure(InsertSubscriptionSessionFailureKind failureKind, string errorMessage)
    {
        return new InsertSubscriptionSessionResult(false, failureKind, errorMessage);
    }
}

public enum GetSubscriptionSessionFailureKind
{
    NotFound,
    DatabaseFailure
}

public sealed record GetSubscriptionSessionResult(
    bool Succeeded,
    ConsumerSubscriptionSessionData? Session,
    GetSubscriptionSessionFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static GetSubscriptionSessionResult Success(ConsumerSubscriptionSessionData session)
    {
        return new GetSubscriptionSessionResult(true, session, null, null);
    }

    public static GetSubscriptionSessionResult Failure(GetSubscriptionSessionFailureKind failureKind, string errorMessage)
    {
        return new GetSubscriptionSessionResult(false, null, failureKind, errorMessage);
    }
}

public enum CloseSubscriptionSessionFailureKind
{
    NotFound,
    DatabaseFailure
}

public sealed record CloseSubscriptionSessionResult(bool Succeeded, CloseSubscriptionSessionFailureKind? FailureKind, string? ErrorMessage)
{
    public static CloseSubscriptionSessionResult Success()
    {
        return new CloseSubscriptionSessionResult(true, null, null);
    }

    public static CloseSubscriptionSessionResult Failure(CloseSubscriptionSessionFailureKind failureKind, string errorMessage)
    {
        return new CloseSubscriptionSessionResult(false, failureKind, errorMessage);
    }
}
