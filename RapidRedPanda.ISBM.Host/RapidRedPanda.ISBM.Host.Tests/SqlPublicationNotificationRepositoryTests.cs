using RapidRedPanda.ISBM.Host.Data;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class SqlPublicationNotificationRepositoryTests
{
    [Fact]
    public async Task ClaimDueSource_UsesAtomicSqlLeaseAndReturnsPublicationRequestAndResponseNotifications()
    {
        string source = await ReadHostFileAsync("Data", "SqlPublicationNotificationRepository.cs");

        Assert.Contains("with (updlock, readpast, rowlock)", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("State = @SendingState", source, StringComparison.Ordinal);
        Assert.Contains("LockOwner = @LockOwner", source, StringComparison.Ordinal);
        Assert.Contains("LockedUntilUtc = dateadd", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("declare @SqlUtcNow datetime2(7) = sysutcdatetime()", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AttemptCount = AttemptCount + 1", source, StringComparison.Ordinal);
        Assert.Contains("NotificationTopics", source, StringComparison.Ordinal);
        Assert.Contains("notification.NotificationServiceType in (@PublicationServiceType, @RequestServiceType, @ResponseServiceType)", source, StringComparison.Ordinal);
        Assert.Contains("State in (@PendingState, @RetryScheduledState)", source, StringComparison.Ordinal);
        Assert.Contains("notification.State = @SendingState", source, StringComparison.Ordinal);
        Assert.Contains("notification.LockedUntilUtc < @SqlUtcNow", source, StringComparison.Ordinal);
        Assert.Contains("notification.DeliveryDeadlineUtc <= @SqlUtcNow", source, StringComparison.Ordinal);
        Assert.Contains("claimed.ProviderRequestSessionId", source, StringComparison.Ordinal);
        Assert.Contains("left join dbo.ConsumerRequests request", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("left join dbo.ConsumerRequestSessions responseSession", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("left join dbo.RequestResponses response", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NotificationServiceTypes.Request => GetRequestSuppressionReason(row)", source, StringComparison.Ordinal);
        Assert.Contains("NotificationServiceTypes.Response => GetResponseSuppressionReason(row)", source, StringComparison.Ordinal);
        Assert.Contains("inserted.ProviderRequestSessionId", source, StringComparison.Ordinal);
        Assert.Contains("claimed.ProviderRequestSessionId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReceiveMessageAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CompleteMessageAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ServiceBusReceiver", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StateUpdateSource_ClearsLeaseFieldsAndBoundsErrors()
    {
        string source = await ReadHostFileAsync("Data", "SqlPublicationNotificationRepository.cs");

        Assert.Contains("State = @DeliveredState", source, StringComparison.Ordinal);
        Assert.Contains("State = @RetryScheduledState", source, StringComparison.Ordinal);
        Assert.Contains("State = @FailedState", source, StringComparison.Ordinal);
        Assert.Contains("State = @SuppressedState", source, StringComparison.Ordinal);
        Assert.Contains("State = @ExpiredState", source, StringComparison.Ordinal);
        Assert.Contains("PublicationNotificationCompletionOutcome.OwnershipLost", source, StringComparison.Ordinal);
        Assert.Contains("and LockOwner = @LockOwner", source, StringComparison.Ordinal);
        Assert.Contains("and LockedUntilUtc >= @SqlUtcNow", source, StringComparison.Ordinal);
        Assert.Contains("LockOwner = null", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LockedUntilUtc = null", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ErrorMaxLength = 2048", source, StringComparison.Ordinal);
        Assert.Contains("SqlDbType.NVarChar, ErrorMaxLength", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetryableFailureSource_RechecksEligibilityAndDeliveryDeadlineBeforeRetry()
    {
        string source = await ReadHostFileAsync("Data", "SqlPublicationNotificationRepository.cs");

        Assert.Contains("CompleteRetryableFailureAsync", source, StringComparison.Ordinal);
        Assert.Contains("publicationSession.SessionState is null or publicationSession.SessionState <> @OpenSessionState", source, StringComparison.Ordinal);
        Assert.Contains("publicationSession.ListenerUrl is null or ltrim(rtrim(publicationSession.ListenerUrl)) = N''", source, StringComparison.Ordinal);
        Assert.Contains("recipient.State is null or recipient.State <> @RecipientPendingState", source, StringComparison.Ordinal);
        Assert.Contains("publication.ExpiredUtc is not null or publication.EffectiveExpiryUtc <= @SqlUtcNow", source, StringComparison.Ordinal);
        Assert.Contains("request.ExpiredUtc is not null or request.EffectiveExpiryUtc <= @SqlUtcNow", source, StringComparison.Ordinal);
        Assert.Contains("request.MessageId is null then @RequestMissingReason", source, StringComparison.Ordinal);
        Assert.Contains("response.MessageId is null or response.RemovedUtc is not null", source, StringComparison.Ordinal);
        Assert.Contains("left join dbo.ProviderRequestSessions requestSession", source, StringComparison.Ordinal);
        Assert.Contains("left join dbo.ConsumerRequestSessions responseSession", source, StringComparison.Ordinal);
        Assert.Contains("requestSession.SessionState <> @OpenSessionState", source, StringComparison.Ordinal);
        Assert.Contains("notification.DeliveryDeadlineUtc <= @SqlUtcNow", source, StringComparison.Ordinal);
        Assert.Contains("notification.AttemptCount >= @MaximumAttempts", source, StringComparison.Ordinal);
        Assert.Contains("dateadd(second, @RetryDelaySeconds, @SqlUtcNow)", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FinalEligibilitySource_RechecksOwnedSendingNotificationBeforeDelivery()
    {
        string source = await ReadHostFileAsync("Data", "SqlPublicationNotificationRepository.cs");

        Assert.Contains("RecheckClaimedEligibilityAsync", source, StringComparison.Ordinal);
        Assert.Contains("notification.State = @SendingState", source, StringComparison.Ordinal);
        Assert.Contains("notification.LockOwner = @LockOwner", source, StringComparison.Ordinal);
        Assert.Contains("notification.LockedUntilUtc >= @SqlUtcNow", source, StringComparison.Ordinal);
        Assert.Contains("PublicationNotificationEligibilityResult.OwnershipLost()", source, StringComparison.Ordinal);
        Assert.Contains("GetSuppressionReason(row)", source, StringComparison.Ordinal);
        Assert.Contains("IsDeliveryWindowExpired(row)", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestSuppressionSource_UsesRequestOnlyBoundedStatePredicates()
    {
        string source = await ReadHostFileAsync("Data", "SqlRequestNotificationSuppression.cs");

        Assert.Contains("NotificationServiceType = @RequestServiceType", source, StringComparison.Ordinal);
        Assert.Contains("ProviderRequestSessionId = @ProviderRequestSessionId", source, StringComparison.Ordinal);
        Assert.Contains("MessageId = @MessageId", source, StringComparison.Ordinal);
        Assert.Contains("State in (@PendingState, @RetryScheduledState)", source, StringComparison.Ordinal);
        Assert.Contains("State = @SuppressedState", source, StringComparison.Ordinal);
        Assert.Contains("ResolvedUtc = @SuppressedUtc", source, StringComparison.Ordinal);
        Assert.Contains("ResolutionReason = @ResolutionReason", source, StringComparison.Ordinal);
        Assert.Contains("NextAttemptUtc = null", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LockOwner = null", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LockedUntilUtc = null", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("State = @SendingState", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NotificationServiceTypes.Publication", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PendingQuerySource_OnlyReturnsPublicationNotifications()
    {
        string source = await ReadHostFileAsync("Data", "SqlPublicationNotificationRepository.cs");

        Assert.Contains("where NotificationServiceType = @PublicationServiceType", source, StringComparison.Ordinal);
        Assert.Contains("ProviderRequestSessionId", source, StringComparison.Ordinal);
        Assert.Contains("new PublicationNotificationData(", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderPublicationInsertSource_WritesPublicationServiceType()
    {
        string source = await ReadHostFileAsync("Data", "SqlProviderPublicationRepository.cs");

        Assert.Contains("NotificationServiceType", source, StringComparison.Ordinal);
        Assert.Contains("@PublicationServiceType", source, StringComparison.Ordinal);
        Assert.Contains("NotificationServiceTypes.Publication", source, StringComparison.Ordinal);
        Assert.Contains("existing.NotificationServiceType = @PublicationServiceType", source, StringComparison.Ordinal);
    }

    private static async Task<string> ReadHostFileAsync(params string[] pathSegments)
    {
        string path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "RapidRedPanda-ISBM-Host",
            Path.Combine(pathSegments)));

        return await File.ReadAllTextAsync(path);
    }
}
