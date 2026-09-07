namespace RapidRedPanda.ISBM.Host.Data;

public sealed record ProviderRequestSessionCreateData(
    Guid SessionId,
    Guid ChannelId,
    string AzureTopicName,
    string AzureSubscriptionName,
    IReadOnlyList<string> RequestedTopics,
    string? AuthenticatedTokenUsername,
    string? ListenerUrl = null);

public sealed record ProviderRequestSessionData(
    Guid SessionId,
    Guid ChannelId,
    string SessionState,
    string AzureTopicName,
    string AzureSubscriptionName,
    IReadOnlyList<string> RequestedTopics,
    string? AuthenticatedTokenUsername,
    DateTime OpenedUtc,
    DateTime? ClosedUtc,
    string? ListenerUrl = null);

public enum InsertProviderRequestSessionFailureKind
{
    DatabaseFailure
}

public sealed record InsertProviderRequestSessionResult(
    bool Succeeded,
    InsertProviderRequestSessionFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static InsertProviderRequestSessionResult Success()
    {
        return new InsertProviderRequestSessionResult(true, null, null);
    }

    public static InsertProviderRequestSessionResult Failure(
        InsertProviderRequestSessionFailureKind failureKind,
        string errorMessage)
    {
        return new InsertProviderRequestSessionResult(false, failureKind, errorMessage);
    }
}

public enum GetProviderRequestSessionFailureKind
{
    NotFound,
    DatabaseFailure
}

public sealed record GetProviderRequestSessionResult(
    bool Succeeded,
    ProviderRequestSessionData? Session,
    GetProviderRequestSessionFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static GetProviderRequestSessionResult Success(ProviderRequestSessionData session)
    {
        return new GetProviderRequestSessionResult(true, session, null, null);
    }

    public static GetProviderRequestSessionResult Failure(
        GetProviderRequestSessionFailureKind failureKind,
        string errorMessage)
    {
        return new GetProviderRequestSessionResult(false, null, failureKind, errorMessage);
    }
}

public enum CloseProviderRequestSessionRepositoryFailureKind
{
    NotFound,
    DatabaseFailure
}

public sealed record CloseProviderRequestSessionRepositoryResult(
    bool Succeeded,
    CloseProviderRequestSessionRepositoryFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static CloseProviderRequestSessionRepositoryResult Success()
    {
        return new CloseProviderRequestSessionRepositoryResult(true, null, null);
    }

    public static CloseProviderRequestSessionRepositoryResult Failure(
        CloseProviderRequestSessionRepositoryFailureKind failureKind,
        string errorMessage)
    {
        return new CloseProviderRequestSessionRepositoryResult(false, failureKind, errorMessage);
    }
}
