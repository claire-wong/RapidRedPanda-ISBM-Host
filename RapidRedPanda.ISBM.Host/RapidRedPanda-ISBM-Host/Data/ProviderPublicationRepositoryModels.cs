namespace RapidRedPanda.ISBM.Host.Data;

public static class ProviderPublicationRecipientStates
{
    public const string Pending = "Pending";
    public const string Outstanding = "Outstanding";
    public const string SkippedExpired = "SkippedExpired";
    public const string Removed = "Removed";
    public const string SubscriptionDeleted = "SubscriptionDeleted";
}

public sealed record ProviderPublicationCreateData(
    Guid MessageId,
    Guid ProviderSessionId,
    Guid ChannelId,
    IReadOnlyList<string> Topics,
    DateTime PublishedUtc,
    DateTime? EffectiveExpiryUtc,
    PersistedMessageContent MessageContent);

public sealed record ProviderPublicationData(
    Guid MessageId,
    Guid ProviderSessionId,
    Guid ChannelId,
    IReadOnlyList<string> Topics,
    PersistedMessageContent MessageContent);

public enum ProviderPublicationRepositoryFailureKind
{
    NotFound,
    ChannelMismatch,
    DatabaseFailure
}

public sealed record ProviderPublicationRepositoryResult(
    bool Succeeded,
    ProviderPublicationRepositoryFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static ProviderPublicationRepositoryResult Success()
    {
        return new ProviderPublicationRepositoryResult(true, null, null);
    }

    public static ProviderPublicationRepositoryResult Failure(
        ProviderPublicationRepositoryFailureKind failureKind,
        string errorMessage)
    {
        return new ProviderPublicationRepositoryResult(false, failureKind, errorMessage);
    }
}

public sealed record PublicationExpirationStateResult(
    bool Succeeded,
    bool IsExpired,
    ProviderPublicationRepositoryFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static PublicationExpirationStateResult Success(bool isExpired)
    {
        return new PublicationExpirationStateResult(true, isExpired, null, null);
    }

    public static PublicationExpirationStateResult Failure(
        ProviderPublicationRepositoryFailureKind failureKind,
        string errorMessage)
    {
        return new PublicationExpirationStateResult(false, false, failureKind, errorMessage);
    }
}

public sealed record ReadProviderPublicationResult(
    bool Succeeded,
    bool HasPublication,
    ProviderPublicationData? Publication,
    ProviderPublicationRepositoryFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static ReadProviderPublicationResult Success(ProviderPublicationData publication)
    {
        return new ReadProviderPublicationResult(true, true, publication, null, null);
    }

    public static ReadProviderPublicationResult NoPublication()
    {
        return new ReadProviderPublicationResult(true, false, null, null, null);
    }

    public static ReadProviderPublicationResult Failure(
        ProviderPublicationRepositoryFailureKind failureKind,
        string errorMessage)
    {
        return new ReadProviderPublicationResult(false, false, null, failureKind, errorMessage);
    }
}

public sealed record RemoveProviderPublicationRecipientResult(
    bool Succeeded,
    Guid? MessageId,
    ProviderPublicationRepositoryFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static RemoveProviderPublicationRecipientResult Success(Guid? messageId)
    {
        return new RemoveProviderPublicationRecipientResult(true, messageId, null, null);
    }

    public static RemoveProviderPublicationRecipientResult Failure(
        ProviderPublicationRepositoryFailureKind failureKind,
        string errorMessage)
    {
        return new RemoveProviderPublicationRecipientResult(false, null, failureKind, errorMessage);
    }
}
