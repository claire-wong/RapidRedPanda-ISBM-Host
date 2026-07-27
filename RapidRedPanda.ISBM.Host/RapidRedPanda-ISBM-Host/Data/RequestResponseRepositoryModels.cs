namespace RapidRedPanda.ISBM.Host.Data;

public sealed record RequestResponseCreateData(
    Guid MessageId,
    Guid RequestMessageId,
    Guid ConsumerRequestSessionId,
    Guid ProviderRequestSessionId,
    Guid ChannelId,
    string MessageContent,
    string? MediaType,
    DateTime CreatedUtc);

public sealed record RequestResponseData(
    long RequestResponseId,
    Guid MessageId,
    Guid RequestMessageId,
    Guid ConsumerRequestSessionId,
    Guid ChannelId,
    string MessageContent,
    string? MediaType);

public enum RequestResponseRepositoryFailureKind
{
    RequestNotFound,
    DatabaseFailure
}

public sealed record RequestResponseRepositoryResult(
    bool Succeeded,
    RequestResponseRepositoryFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static RequestResponseRepositoryResult Success()
    {
        return new RequestResponseRepositoryResult(true, null, null);
    }

    public static RequestResponseRepositoryResult Failure(
        RequestResponseRepositoryFailureKind failureKind,
        string errorMessage)
    {
        return new RequestResponseRepositoryResult(false, failureKind, errorMessage);
    }
}

public sealed record ActiveRequestMatchResult(
    bool Succeeded,
    bool HasActiveRequest,
    Guid? ConsumerRequestSessionId,
    RequestResponseRepositoryFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static ActiveRequestMatchResult Success(Guid consumerRequestSessionId)
    {
        return new ActiveRequestMatchResult(true, true, consumerRequestSessionId, null, null);
    }

    public static ActiveRequestMatchResult NoMatch()
    {
        return new ActiveRequestMatchResult(true, false, null, null, null);
    }

    public static ActiveRequestMatchResult Failure(
        RequestResponseRepositoryFailureKind failureKind,
        string errorMessage)
    {
        return new ActiveRequestMatchResult(false, false, null, failureKind, errorMessage);
    }
}

public sealed record ReadRequestResponseResult(
    bool Succeeded,
    bool HasResponse,
    RequestResponseData? Response,
    RequestResponseRepositoryFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static ReadRequestResponseResult Success(RequestResponseData response)
    {
        return new ReadRequestResponseResult(true, true, response, null, null);
    }

    public static ReadRequestResponseResult NoResponse()
    {
        return new ReadRequestResponseResult(true, false, null, null, null);
    }

    public static ReadRequestResponseResult Failure(
        RequestResponseRepositoryFailureKind failureKind,
        string errorMessage)
    {
        return new ReadRequestResponseResult(false, false, null, failureKind, errorMessage);
    }
}
