namespace RapidRedPanda.ISBM.Host.Data;

public sealed record ConsumerRequestCreateData(
    Guid MessageId,
    Guid ConsumerSessionId,
    Guid ChannelId,
    IReadOnlyList<string> Topics,
    DateTime CreatedUtc,
    DateTime? EffectiveExpiryUtc);

public enum ConsumerRequestRepositoryFailureKind
{
    DatabaseFailure
}

public sealed record ConsumerRequestRepositoryResult(
    bool Succeeded,
    ConsumerRequestRepositoryFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static ConsumerRequestRepositoryResult Success()
    {
        return new ConsumerRequestRepositoryResult(true, null, null);
    }

    public static ConsumerRequestRepositoryResult Failure(
        ConsumerRequestRepositoryFailureKind failureKind,
        string errorMessage)
    {
        return new ConsumerRequestRepositoryResult(false, failureKind, errorMessage);
    }
}

public sealed record RequestExpirationStateResult(
    bool Succeeded,
    bool IsExpired,
    ConsumerRequestRepositoryFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static RequestExpirationStateResult Success(bool isExpired)
    {
        return new RequestExpirationStateResult(true, isExpired, null, null);
    }

    public static RequestExpirationStateResult Failure(
        ConsumerRequestRepositoryFailureKind failureKind,
        string errorMessage)
    {
        return new RequestExpirationStateResult(false, false, failureKind, errorMessage);
    }
}
