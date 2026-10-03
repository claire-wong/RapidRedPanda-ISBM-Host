namespace RapidRedPanda.ISBM.Host.Data;

public sealed record ConsumerRequestCreateData(
    Guid MessageId,
    Guid ConsumerSessionId,
    Guid ChannelId,
    IReadOnlyList<string> Topics,
    DateTime CreatedUtc,
    DateTime? EffectiveExpiryUtc,
    PersistedMessageContent MessageContent);

public static class ConsumerRequestRecipientStates
{
    public const string Outstanding = "Outstanding";
    public const string Removed = "Removed";
}

public sealed record ConsumerRequestData(
    Guid MessageId,
    Guid ConsumerSessionId,
    Guid ChannelId,
    string Topic,
    PersistedMessageContent MessageContent);

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

public sealed record ReadConsumerRequestResult(
    bool Succeeded,
    bool HasRequest,
    ConsumerRequestData? Request,
    ConsumerRequestRepositoryFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static ReadConsumerRequestResult Success(ConsumerRequestData request)
    {
        return new ReadConsumerRequestResult(true, true, request, null, null);
    }

    public static ReadConsumerRequestResult NoRequest()
    {
        return new ReadConsumerRequestResult(true, false, null, null, null);
    }

    public static ReadConsumerRequestResult Failure(
        ConsumerRequestRepositoryFailureKind failureKind,
        string errorMessage)
    {
        return new ReadConsumerRequestResult(false, false, null, failureKind, errorMessage);
    }
}

public sealed record RemoveConsumerRequestRecipientResult(
    bool Succeeded,
    Guid? RequestMessageId,
    ConsumerRequestRepositoryFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static RemoveConsumerRequestRecipientResult Success(Guid? requestMessageId)
    {
        return new RemoveConsumerRequestRecipientResult(true, requestMessageId, null, null);
    }

    public static RemoveConsumerRequestRecipientResult Failure(
        ConsumerRequestRepositoryFailureKind failureKind,
        string errorMessage)
    {
        return new RemoveConsumerRequestRecipientResult(false, null, failureKind, errorMessage);
    }
}

public sealed record RequestResponseEligibilityResult(
    bool Succeeded,
    bool CanRespond,
    Guid? ConsumerRequestSessionId,
    ConsumerRequestRepositoryFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static RequestResponseEligibilityResult Success(Guid consumerRequestSessionId)
    {
        return new RequestResponseEligibilityResult(true, true, consumerRequestSessionId, null, null);
    }

    public static RequestResponseEligibilityResult NoMatch()
    {
        return new RequestResponseEligibilityResult(true, false, null, null, null);
    }

    public static RequestResponseEligibilityResult Failure(
        ConsumerRequestRepositoryFailureKind failureKind,
        string errorMessage)
    {
        return new RequestResponseEligibilityResult(false, false, null, failureKind, errorMessage);
    }
}
