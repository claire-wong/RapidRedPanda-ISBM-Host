namespace RapidRedPanda.ISBM.Host.Data;

public static class PublicationNotificationStates
{
    public const string Pending = "Pending";
    public const string Sending = "Sending";
    public const string Delivered = "Delivered";
    public const string RetryScheduled = "RetryScheduled";
    public const string Failed = "Failed";
    public const string Suppressed = "Suppressed";
    public const string Expired = "Expired";
}

public static class NotificationServiceTypes
{
    public const string Publication = "Publication";
    public const string Request = "Request";
}

public static class PublicationNotificationSuppressionReasons
{
    public const string SessionClosed = "SessionClosed";
    public const string RecipientNotPending = "RecipientNotPending";
    public const string PublicationExpired = "PublicationExpired";
    public const string RequestMissing = "RequestMissing";
    public const string RequestExpired = "RequestExpired";
    public const string ListenerMissing = "ListenerMissing";
    public const string DeliveryWindowExpired = "DeliveryWindowExpired";
    public const string InvalidNotificationShape = "InvalidNotificationShape";
}

public sealed record PublicationNotificationData(
    Guid PublicationNotificationId,
    string ServiceType,
    Guid MessageId,
    Guid? ConsumerSessionId,
    Guid? ProviderRequestSessionId,
    string ListenerUrl,
    IReadOnlyList<string> Topics,
    string State,
    int AttemptCount,
    DateTime? NextAttemptUtc,
    DateTime CreatedUtc,
    DateTime? DeliveryDeadlineUtc,
    DateTime? ResolvedUtc);

public sealed record ClaimedPublicationNotification(
    Guid NotificationId,
    string ServiceType,
    Guid MessageId,
    Guid? ConsumerSessionId,
    Guid? ProviderRequestSessionId,
    string ListenerUrl,
    IReadOnlyList<string> Topics,
    int AttemptCount,
    DateTime? NextAttemptUtc,
    DateTime CreatedUtc,
    DateTime? DeliveryDeadlineUtc,
    bool IsEligible,
    string? SuppressionReason,
    bool IsDeliveryWindowExpired);

public enum PublicationNotificationCompletionOutcome
{
    Delivered,
    RetryScheduled,
    Failed,
    Suppressed,
    Expired,
    OwnershipLost
}

public sealed record PublicationNotificationCompletionResult(
    bool Succeeded,
    PublicationNotificationCompletionOutcome? Outcome,
    string? ResolutionReason,
    ProviderPublicationRepositoryFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static PublicationNotificationCompletionResult Success(
        PublicationNotificationCompletionOutcome outcome,
        string? resolutionReason = null)
    {
        return new PublicationNotificationCompletionResult(true, outcome, resolutionReason, null, null);
    }

    public static PublicationNotificationCompletionResult Failure(
        ProviderPublicationRepositoryFailureKind failureKind,
        string errorMessage)
    {
        return new PublicationNotificationCompletionResult(false, null, null, failureKind, errorMessage);
    }
}

public sealed record PublicationNotificationRepositoryResult(
    bool Succeeded,
    ProviderPublicationRepositoryFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static PublicationNotificationRepositoryResult Success()
    {
        return new PublicationNotificationRepositoryResult(true, null, null);
    }

    public static PublicationNotificationRepositoryResult Failure(
        ProviderPublicationRepositoryFailureKind failureKind,
        string errorMessage)
    {
        return new PublicationNotificationRepositoryResult(false, failureKind, errorMessage);
    }
}

public sealed record PublicationNotificationClaimResult(
    bool Succeeded,
    IReadOnlyList<ClaimedPublicationNotification> Notifications,
    ProviderPublicationRepositoryFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static PublicationNotificationClaimResult Success(IReadOnlyList<ClaimedPublicationNotification> notifications)
    {
        return new PublicationNotificationClaimResult(true, notifications, null, null);
    }

    public static PublicationNotificationClaimResult Failure(
        ProviderPublicationRepositoryFailureKind failureKind,
        string errorMessage)
    {
        return new PublicationNotificationClaimResult(false, [], failureKind, errorMessage);
    }
}

public sealed record PublicationNotificationQueryResult(
    bool Succeeded,
    IReadOnlyList<PublicationNotificationData> Notifications,
    ProviderPublicationRepositoryFailureKind? FailureKind,
    string? ErrorMessage)
{
    public static PublicationNotificationQueryResult Success(IReadOnlyList<PublicationNotificationData> notifications)
    {
        return new PublicationNotificationQueryResult(true, notifications, null, null);
    }

    public static PublicationNotificationQueryResult Failure(
        ProviderPublicationRepositoryFailureKind failureKind,
        string errorMessage)
    {
        return new PublicationNotificationQueryResult(false, [], failureKind, errorMessage);
    }
}
