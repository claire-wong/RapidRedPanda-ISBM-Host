namespace RapidRedPanda.ISBM.Host.Data;

public interface IPublicationNotificationRepository
{
    Task<PublicationNotificationClaimResult> ClaimDueAsync(
        string lockOwner,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<PublicationNotificationEligibilityResult> RecheckClaimedEligibilityAsync(
        Guid notificationId,
        string lockOwner,
        CancellationToken cancellationToken);

    Task<PublicationNotificationCompletionResult> MarkDeliveredAsync(
        Guid notificationId,
        string lockOwner,
        int statusCode,
        CancellationToken cancellationToken);

    Task<PublicationNotificationCompletionResult> ScheduleRetryAsync(
        Guid notificationId,
        string lockOwner,
        TimeSpan retryDelay,
        int? statusCode,
        string error,
        CancellationToken cancellationToken);

    Task<PublicationNotificationCompletionResult> CompleteRetryableFailureAsync(
        Guid notificationId,
        string lockOwner,
        int maximumAttempts,
        TimeSpan retryDelay,
        int? statusCode,
        string error,
        CancellationToken cancellationToken);

    Task<PublicationNotificationCompletionResult> MarkFailedAsync(
        Guid notificationId,
        string lockOwner,
        int? statusCode,
        string error,
        CancellationToken cancellationToken);

    Task<PublicationNotificationCompletionResult> MarkSuppressedAsync(
        Guid notificationId,
        string lockOwner,
        string resolutionReason,
        CancellationToken cancellationToken);

    Task<PublicationNotificationCompletionResult> MarkExpiredAsync(
        Guid notificationId,
        string lockOwner,
        CancellationToken cancellationToken);

    Task<PublicationNotificationSuppressionResult> SuppressRequestNotificationsForSessionAsync(
        Guid providerRequestSessionId,
        DateTime suppressedUtc,
        CancellationToken cancellationToken);

    Task<PublicationNotificationSuppressionResult> SuppressRequestNotificationsForMessageAsync(
        Guid requestMessageId,
        DateTime suppressedUtc,
        CancellationToken cancellationToken);

    Task<PublicationNotificationQueryResult> GetPendingPublicationNotificationsAsync(
        int maxCount,
        CancellationToken cancellationToken);
}
