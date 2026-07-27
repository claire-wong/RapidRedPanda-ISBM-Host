namespace RapidRedPanda.ISBM.Host.Data;

public static class ProviderPublicationSessionStates
{
    public const string Open = "Open";
    public const string Closed = "Closed";
}

public sealed record ProviderPublicationSessionCreateData(
    Guid SessionId,
    Guid ChannelId,
    string? AuthenticatedTokenUsername);

public sealed record ProviderPublicationSessionData(
    Guid SessionId,
    Guid ChannelId,
    string SessionState,
    string? AuthenticatedTokenUsername,
    DateTime CreatedUtc,
    DateTime? ClosedUtc);

public enum InsertProviderPublicationSessionFailureKind
{
    DatabaseFailure
}

public sealed record InsertProviderPublicationSessionResult(
    bool Succeeded,
    InsertProviderPublicationSessionFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static InsertProviderPublicationSessionResult Success()
    {
        return new InsertProviderPublicationSessionResult(true, null, null);
    }

    public static InsertProviderPublicationSessionResult Failure(
        InsertProviderPublicationSessionFailureKind failureKind,
        string errorMessage)
    {
        return new InsertProviderPublicationSessionResult(false, failureKind, errorMessage);
    }
}

public enum GetProviderPublicationSessionFailureKind
{
    NotFound,
    DatabaseFailure
}

public sealed record GetProviderPublicationSessionResult(
    bool Succeeded,
    ProviderPublicationSessionData? Session,
    GetProviderPublicationSessionFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static GetProviderPublicationSessionResult Success(ProviderPublicationSessionData session)
    {
        return new GetProviderPublicationSessionResult(true, session, null, null);
    }

    public static GetProviderPublicationSessionResult Failure(
        GetProviderPublicationSessionFailureKind failureKind,
        string errorMessage)
    {
        return new GetProviderPublicationSessionResult(false, null, failureKind, errorMessage);
    }
}

public enum CloseProviderPublicationSessionFailureKind
{
    NotFound,
    DatabaseFailure
}

public sealed record CloseProviderPublicationSessionResult(
    bool Succeeded,
    CloseProviderPublicationSessionFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static CloseProviderPublicationSessionResult Success()
    {
        return new CloseProviderPublicationSessionResult(true, null, null);
    }

    public static CloseProviderPublicationSessionResult Failure(
        CloseProviderPublicationSessionFailureKind failureKind,
        string errorMessage)
    {
        return new CloseProviderPublicationSessionResult(false, failureKind, errorMessage);
    }
}
