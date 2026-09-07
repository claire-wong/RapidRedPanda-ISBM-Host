namespace RapidRedPanda.ISBM.Host.Data;

public static class ConsumerRequestSessionStates
{
    public const string Open = "Open";
    public const string Closed = "Closed";
}

public sealed record ConsumerRequestSessionCreateData(
    Guid SessionId,
    Guid ChannelId,
    string? AuthenticatedTokenUsername,
    string? ListenerUrl = null);

public sealed record ConsumerRequestSessionData(
    Guid SessionId,
    Guid ChannelId,
    string SessionState,
    string? AuthenticatedTokenUsername,
    DateTime CreatedUtc,
    DateTime? ClosedUtc,
    string? ListenerUrl = null);

public enum InsertConsumerRequestSessionFailureKind
{
    DatabaseFailure
}

public sealed record InsertConsumerRequestSessionResult(
    bool Succeeded,
    InsertConsumerRequestSessionFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static InsertConsumerRequestSessionResult Success()
    {
        return new InsertConsumerRequestSessionResult(true, null, null);
    }

    public static InsertConsumerRequestSessionResult Failure(
        InsertConsumerRequestSessionFailureKind failureKind,
        string errorMessage)
    {
        return new InsertConsumerRequestSessionResult(false, failureKind, errorMessage);
    }
}

public enum GetConsumerRequestSessionFailureKind
{
    NotFound,
    DatabaseFailure
}

public sealed record GetConsumerRequestSessionResult(
    bool Succeeded,
    ConsumerRequestSessionData? Session,
    GetConsumerRequestSessionFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static GetConsumerRequestSessionResult Success(ConsumerRequestSessionData session)
    {
        return new GetConsumerRequestSessionResult(true, session, null, null);
    }

    public static GetConsumerRequestSessionResult Failure(
        GetConsumerRequestSessionFailureKind failureKind,
        string errorMessage)
    {
        return new GetConsumerRequestSessionResult(false, null, failureKind, errorMessage);
    }
}

public enum CloseConsumerRequestSessionFailureKind
{
    NotFound,
    DatabaseFailure
}

public sealed record CloseConsumerRequestSessionResult(
    bool Succeeded,
    CloseConsumerRequestSessionFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static CloseConsumerRequestSessionResult Success()
    {
        return new CloseConsumerRequestSessionResult(true, null, null);
    }

    public static CloseConsumerRequestSessionResult Failure(
        CloseConsumerRequestSessionFailureKind failureKind,
        string errorMessage)
    {
        return new CloseConsumerRequestSessionResult(false, failureKind, errorMessage);
    }
}
