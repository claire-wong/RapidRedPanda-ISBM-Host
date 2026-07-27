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
    DateTime? EffectiveExpiryUtc);

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
