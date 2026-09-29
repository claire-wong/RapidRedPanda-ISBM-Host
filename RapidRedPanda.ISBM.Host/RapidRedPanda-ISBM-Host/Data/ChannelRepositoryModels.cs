namespace RapidRedPanda.ISBM.Host.Data;

public sealed record ChannelCreateToken(string Username, string NormalizedUsername, string Password);

public sealed record ChannelSecurityTokenData(string TokenType, string Username);

public sealed record ChannelCreateRequestData(
    Guid ChannelId,
    string ChannelUri,
    string ChannelType,
    string? Description,
    IReadOnlyList<ChannelCreateToken> SecurityTokens,
    string AzureTopicName);

public sealed record ChannelData(Guid ChannelId, string ChannelUri, string ChannelType, string? Description, string AzureTopicName);

public sealed record ChannelDetailData(
    Guid ChannelId,
    string ChannelUri,
    string ChannelType,
    string? Description,
    string AzureTopicName,
    IReadOnlyList<ChannelSecurityTokenData> SecurityTokens);

public sealed record ChannelCredentials(string NormalizedUsername, string Password);

public sealed record ChannelSubscriptionData(
    Guid ChannelId,
    string ChannelUri,
    string ChannelType,
    string AzureTopicName,
    string? AuthenticatedTokenUsername);

public sealed record ChannelTopicBindingData(
    Guid ChannelId,
    string ChannelUri,
    string ChannelType,
    string AzureTopicName);

public enum CreateChannelFailureKind
{
    DuplicateChannelUri,
    DuplicateTokenUsername,
    InvalidChannelType,
    DatabaseFailure
}

public sealed record CreateChannelResult(bool Succeeded, ChannelData? Channel, CreateChannelFailureKind? FailureKind, string? ErrorMessage)
{
    public static CreateChannelResult Success(ChannelData channel)
    {
        return new CreateChannelResult(true, channel, null, null);
    }

    public static CreateChannelResult Failure(CreateChannelFailureKind failureKind, string errorMessage)
    {
        return new CreateChannelResult(false, null, failureKind, errorMessage);
    }
}

public enum DeleteChannelFailureKind
{
    NotFound,
    DatabaseFailure
}

public enum GetChannelTopicBindingFailureKind
{
    NotFound,
    DatabaseFailure
}

public sealed record GetChannelTopicBindingResult(
    bool Succeeded,
    ChannelTopicBindingData? Channel,
    GetChannelTopicBindingFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static GetChannelTopicBindingResult Success(ChannelTopicBindingData channel)
    {
        return new GetChannelTopicBindingResult(true, channel, null, null);
    }

    public static GetChannelTopicBindingResult Failure(GetChannelTopicBindingFailureKind failureKind, string errorMessage)
    {
        return new GetChannelTopicBindingResult(false, null, failureKind, errorMessage);
    }
}

public sealed record DeleteChannelResult(bool Succeeded, DeleteChannelFailureKind? FailureKind, string? ErrorMessage)
{
    public static DeleteChannelResult Success()
    {
        return new DeleteChannelResult(true, null, null);
    }

    public static DeleteChannelResult Failure(DeleteChannelFailureKind failureKind, string errorMessage)
    {
        return new DeleteChannelResult(false, failureKind, errorMessage);
    }
}

public enum GetChannelFailureKind
{
    NotFound,
    DatabaseFailure
}

public sealed record GetChannelResult(bool Succeeded, ChannelDetailData? Channel, GetChannelFailureKind? FailureKind, string? ErrorMessage)
{
    public static GetChannelResult Success(ChannelDetailData channel)
    {
        return new GetChannelResult(true, channel, null, null);
    }

    public static GetChannelResult Failure(GetChannelFailureKind failureKind, string errorMessage)
    {
        return new GetChannelResult(false, null, failureKind, errorMessage);
    }
}

public enum GetChannelForSubscriptionFailureKind
{
    NotFound,
    InvalidChannelType,
    SecurityTokenInvalid,
    DatabaseFailure
}

public sealed record GetChannelForSubscriptionResult(
    bool Succeeded,
    ChannelSubscriptionData? Channel,
    GetChannelForSubscriptionFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static GetChannelForSubscriptionResult Success(ChannelSubscriptionData channel)
    {
        return new GetChannelForSubscriptionResult(true, channel, null, null);
    }

    public static GetChannelForSubscriptionResult Failure(GetChannelForSubscriptionFailureKind failureKind, string errorMessage)
    {
        return new GetChannelForSubscriptionResult(false, null, failureKind, errorMessage);
    }
}

public enum ValidateChannelCredentialsFailureKind
{
    NotFound,
    SecurityTokenInvalid,
    DatabaseFailure
}

public sealed record ValidateChannelCredentialsResult(
    bool Succeeded,
    ValidateChannelCredentialsFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static ValidateChannelCredentialsResult Success()
    {
        return new ValidateChannelCredentialsResult(true, null, null);
    }

    public static ValidateChannelCredentialsResult Failure(
        ValidateChannelCredentialsFailureKind failureKind,
        string errorMessage)
    {
        return new ValidateChannelCredentialsResult(false, failureKind, errorMessage);
    }
}

public enum SecurityTokenMutationFailureKind
{
    NotFound,
    OpenChannel,
    SecurityTokenNotFound,
    DatabaseFailure
}

public sealed record SecurityTokenMutationResult(bool Succeeded, SecurityTokenMutationFailureKind? FailureKind, string? ErrorMessage)
{
    public static SecurityTokenMutationResult Success()
    {
        return new SecurityTokenMutationResult(true, null, null);
    }

    public static SecurityTokenMutationResult Failure(SecurityTokenMutationFailureKind failureKind, string errorMessage)
    {
        return new SecurityTokenMutationResult(false, failureKind, errorMessage);
    }
}
