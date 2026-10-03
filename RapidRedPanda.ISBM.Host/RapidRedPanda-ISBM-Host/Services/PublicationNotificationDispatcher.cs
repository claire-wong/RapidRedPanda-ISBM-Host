using Microsoft.Extensions.Options;
using RapidRedPanda.ISBM.Host.Data;

namespace RapidRedPanda.ISBM.Host.Services;

public sealed class PublicationNotificationDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPublicationNotificationClient _client;
    private readonly PublicationNotificationOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PublicationNotificationDispatcher> _logger;
    private readonly string _lockOwner;

    public PublicationNotificationDispatcher(
        IServiceScopeFactory scopeFactory,
        IPublicationNotificationClient client,
        IOptions<PublicationNotificationOptions> options,
        TimeProvider timeProvider,
        ILogger<PublicationNotificationDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _lockOwner = $"{Environment.MachineName}:{Guid.NewGuid():N}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Publication notification dispatcher is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IPublicationNotificationRepository repository =
                    scope.ServiceProvider.GetRequiredService<IPublicationNotificationRepository>();

                PublicationNotificationClaimResult claimResult = await repository.ClaimDueAsync(
                    _lockOwner,
                    _options.BatchSize,
                    TimeSpan.FromSeconds(_options.LeaseDurationSeconds),
                    stoppingToken);

                if (!claimResult.Succeeded)
                {
                    _logger.LogError("Failed to claim publication notifications: {ErrorMessage}.", claimResult.ErrorMessage);
                    await DelayAsync(_options.ErrorPollIntervalSeconds, stoppingToken);
                    continue;
                }

                if (claimResult.Notifications.Count == 0)
                {
                    await DelayAsync(_options.IdlePollIntervalSeconds, stoppingToken);
                    continue;
                }

                _logger.LogInformation(
                    "Claimed {Count} publication notification(s).",
                    claimResult.Notifications.Count);

                await ProcessBatchAsync(repository, claimResult.Notifications, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Publication notification dispatcher loop failed.");
                await DelayAsync(_options.ErrorPollIntervalSeconds, stoppingToken);
            }
        }
    }

    private async Task ProcessBatchAsync(
        IPublicationNotificationRepository repository,
        IReadOnlyList<ClaimedPublicationNotification> notifications,
        CancellationToken stoppingToken)
    {
        using SemaphoreSlim semaphore = new(_options.MaxConcurrency);
        List<Task> tasks = [];

        foreach (ClaimedPublicationNotification notification in notifications)
        {
            await semaphore.WaitAsync(stoppingToken);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    try
                    {
                        await ProcessNotificationAsync(repository, notification, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(
                            exception,
                            "Publication notification {NotificationId} processing failed before completion.",
                            notification.NotificationId);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }, stoppingToken));
        }

        await Task.WhenAll(tasks);
    }

    private async Task ProcessNotificationAsync(
        IPublicationNotificationRepository repository,
        ClaimedPublicationNotification notification,
        CancellationToken stoppingToken)
    {
        if (!TryValidateNotificationShape(notification, out string? shapeError))
        {
            PublicationNotificationCompletionResult completion = await repository.MarkFailedAsync(
                notification.NotificationId,
                _lockOwner,
                statusCode: null,
                shapeError,
                stoppingToken);
            LogCompletionResult(notification, completion, "invalid shape failure");
            _logger.LogWarning(
                "Failed malformed notification {NotificationId} terminally. ServiceType: {ServiceType}. MessageId: {MessageId}.",
                notification.NotificationId,
                notification.ServiceType,
                notification.MessageId);
            return;
        }

        Guid recipientSessionId = GetRecipientSessionId(notification);

        if (!notification.IsEligible)
        {
            PublicationNotificationCompletionResult completion = await repository.MarkSuppressedAsync(
                notification.NotificationId,
                _lockOwner,
                notification.SuppressionReason ?? PublicationNotificationSuppressionReasons.RecipientNotPending,
                stoppingToken);
            LogCompletionResult(notification, completion, "suppression");
            _logger.LogInformation(
                "Suppressed notification {NotificationId}. ServiceType: {ServiceType}. MessageId: {MessageId}. RecipientSessionId: {RecipientSessionId}. Reason: {Reason}.",
                notification.NotificationId,
                notification.ServiceType,
                notification.MessageId,
                recipientSessionId,
                notification.SuppressionReason);
            return;
        }

        if (notification.IsDeliveryWindowExpired)
        {
            PublicationNotificationCompletionResult completion = await repository.MarkExpiredAsync(
                notification.NotificationId,
                _lockOwner,
                stoppingToken);
            LogCompletionResult(notification, completion, "expiration");
            _logger.LogInformation(
                "Expired notification {NotificationId}. ServiceType: {ServiceType}. MessageId: {MessageId}. RecipientSessionId: {RecipientSessionId}.",
                notification.NotificationId,
                notification.ServiceType,
                notification.MessageId,
                recipientSessionId);
            return;
        }

        PublicationNotificationEligibilityResult eligibility = await repository.RecheckClaimedEligibilityAsync(
            notification.NotificationId,
            _lockOwner,
            stoppingToken);

        if (!eligibility.Succeeded)
        {
            _logger.LogWarning(
                "Skipped notification {NotificationId} delivery because final eligibility recheck failed. ServiceType: {ServiceType}. MessageId: {MessageId}.",
                notification.NotificationId,
                notification.ServiceType,
                notification.MessageId);
            return;
        }

        if (!eligibility.HasOwnership)
        {
            _logger.LogInformation(
                "Skipped notification {NotificationId} delivery because ownership was lost before send. ServiceType: {ServiceType}. MessageId: {MessageId}.",
                notification.NotificationId,
                notification.ServiceType,
                notification.MessageId);
            return;
        }

        if (!eligibility.IsEligible)
        {
            PublicationNotificationCompletionResult completion = await repository.MarkSuppressedAsync(
                notification.NotificationId,
                _lockOwner,
                eligibility.SuppressionReason ?? PublicationNotificationSuppressionReasons.RecipientNotPending,
                stoppingToken);
            LogCompletionResult(notification, completion, "final eligibility suppression");
            _logger.LogInformation(
                "Suppressed notification {NotificationId} after final eligibility recheck. ServiceType: {ServiceType}. MessageId: {MessageId}. RecipientSessionId: {RecipientSessionId}. Reason: {Reason}.",
                notification.NotificationId,
                notification.ServiceType,
                notification.MessageId,
                recipientSessionId,
                eligibility.SuppressionReason);
            return;
        }

        if (eligibility.IsDeliveryWindowExpired)
        {
            PublicationNotificationCompletionResult completion = await repository.MarkExpiredAsync(
                notification.NotificationId,
                _lockOwner,
                stoppingToken);
            LogCompletionResult(notification, completion, "final eligibility expiration");
            _logger.LogInformation(
                "Expired notification {NotificationId} after final eligibility recheck. ServiceType: {ServiceType}. MessageId: {MessageId}. RecipientSessionId: {RecipientSessionId}.",
                notification.NotificationId,
                notification.ServiceType,
                notification.MessageId,
                recipientSessionId);
            return;
        }

        _logger.LogInformation(
            "Delivering notification {NotificationId}. ServiceType: {ServiceType}. MessageId: {MessageId}. RecipientSessionId: {RecipientSessionId}. LockOwner: {LockOwner}. Attempt: {Attempt}.",
            notification.NotificationId,
            notification.ServiceType,
            notification.MessageId,
            recipientSessionId,
            _lockOwner,
            notification.AttemptCount);

        PublicationNotificationDeliveryResult deliveryResult = await _client.SendAsync(notification, stoppingToken);

        switch (deliveryResult.Outcome)
        {
            case PublicationNotificationDeliveryOutcome.Delivered:
                PublicationNotificationCompletionResult deliveredCompletion = await repository.MarkDeliveredAsync(
                    notification.NotificationId,
                    _lockOwner,
                    deliveryResult.StatusCode ?? StatusCodes.Status204NoContent,
                    stoppingToken);
                LogCompletionResult(notification, deliveredCompletion, "delivery");
                _logger.LogInformation(
                    "Delivered notification {NotificationId}. ServiceType: {ServiceType}. MessageId: {MessageId}. RecipientSessionId: {RecipientSessionId}.",
                    notification.NotificationId,
                    notification.ServiceType,
                    notification.MessageId,
                    recipientSessionId);
                break;

            case PublicationNotificationDeliveryOutcome.RetryableFailure:
                await HandleRetryableFailureAsync(repository, notification, deliveryResult, stoppingToken);
                break;

            case PublicationNotificationDeliveryOutcome.TerminalFailure:
                PublicationNotificationCompletionResult failedCompletion = await repository.MarkFailedAsync(
                    notification.NotificationId,
                    _lockOwner,
                    deliveryResult.StatusCode,
                    SanitizeError(deliveryResult.Error),
                    stoppingToken);
                LogCompletionResult(notification, failedCompletion, "terminal failure");
                _logger.LogWarning(
                    "Failed notification {NotificationId} terminally. ServiceType: {ServiceType}. MessageId: {MessageId}. RecipientSessionId: {RecipientSessionId}. StatusCode: {StatusCode}.",
                    notification.NotificationId,
                    notification.ServiceType,
                    notification.MessageId,
                    recipientSessionId,
                    deliveryResult.StatusCode);
                break;

            case PublicationNotificationDeliveryOutcome.ShutdownCanceled:
                PublicationNotificationCompletionResult shutdownCompletion = await repository.ScheduleRetryAsync(
                    notification.NotificationId,
                    _lockOwner,
                    TimeSpan.Zero,
                    deliveryResult.StatusCode,
                    SanitizeError(deliveryResult.Error),
                    CancellationToken.None);
                LogCompletionResult(notification, shutdownCompletion, "shutdown retry scheduling");
                break;
        }
    }

    private async Task HandleRetryableFailureAsync(
        IPublicationNotificationRepository repository,
        ClaimedPublicationNotification notification,
        PublicationNotificationDeliveryResult deliveryResult,
        CancellationToken stoppingToken)
    {
        TimeSpan retryDelay = CalculateRetryDelay(notification.AttemptCount, _options);
        PublicationNotificationCompletionResult completion = await repository.CompleteRetryableFailureAsync(
            notification.NotificationId,
            _lockOwner,
            _options.MaximumAttempts,
            retryDelay,
            deliveryResult.StatusCode,
            SanitizeError(deliveryResult.Error),
            stoppingToken);
        LogCompletionResult(notification, completion, "retryable failure");
        Guid recipientSessionId = GetRecipientSessionId(notification);

        _logger.LogInformation(
            "Completed retryable failure for notification {NotificationId}. ServiceType: {ServiceType}. MessageId: {MessageId}. RecipientSessionId: {RecipientSessionId}. Outcome: {Outcome}. StatusCode: {StatusCode}. RetryDelaySeconds: {RetryDelaySeconds}.",
            notification.NotificationId,
            notification.ServiceType,
            notification.MessageId,
            recipientSessionId,
            completion.Outcome,
            deliveryResult.StatusCode,
            retryDelay.TotalSeconds);
    }

    internal static TimeSpan CalculateRetryDelay(int attemptCount, PublicationNotificationOptions options)
    {
        int exponent = Math.Max(0, attemptCount - 1);
        double nominalSeconds = options.InitialRetryDelaySeconds * Math.Pow(options.BackoffMultiplier, exponent);
        nominalSeconds = Math.Min(nominalSeconds, options.MaximumRetryDelaySeconds);

        if (options.JitterPercent > 0)
        {
            double jitter = nominalSeconds * options.JitterPercent / 100d;
            nominalSeconds += Random.Shared.NextDouble() * jitter;
        }

        nominalSeconds = Math.Min(nominalSeconds, options.MaximumRetryDelaySeconds);
        return TimeSpan.FromSeconds(nominalSeconds);
    }

    private DateTime GetUtcNow()
    {
        return _timeProvider.GetUtcNow().UtcDateTime;
    }

    private static bool TryValidateNotificationShape(
        ClaimedPublicationNotification notification,
        out string error)
    {
        error = "Invalid notification shape.";
        if (string.Equals(notification.ServiceType, NotificationServiceTypes.Publication, StringComparison.Ordinal))
        {
            if (notification.ConsumerSessionId is not null &&
                notification.ProviderRequestSessionId is null &&
                notification.ConsumerRequestSessionId is null &&
                notification.RequestMessageId is null)
            {
                return true;
            }

            error = "Invalid publication notification shape.";
            return false;
        }

        if (string.Equals(notification.ServiceType, NotificationServiceTypes.Request, StringComparison.Ordinal))
        {
            if (notification.ProviderRequestSessionId is not null &&
                notification.ConsumerSessionId is null &&
                notification.ConsumerRequestSessionId is null &&
                notification.RequestMessageId is null)
            {
                return true;
            }

            error = "Invalid request notification shape.";
            return false;
        }

        if (string.Equals(notification.ServiceType, NotificationServiceTypes.Response, StringComparison.Ordinal))
        {
            if (notification.ConsumerRequestSessionId is not null &&
                notification.RequestMessageId is not null &&
                notification.ConsumerSessionId is null &&
                notification.ProviderRequestSessionId is null)
            {
                return true;
            }

            error = "Invalid response notification shape.";
            return false;
        }

        error = "Unsupported notification service type.";
        return false;
    }

    private static Guid GetRecipientSessionId(ClaimedPublicationNotification notification)
    {
        return notification.ServiceType switch
        {
            NotificationServiceTypes.Request => notification.ProviderRequestSessionId!.Value,
            NotificationServiceTypes.Response => notification.ConsumerRequestSessionId!.Value,
            _ => notification.ConsumerSessionId!.Value
        };
    }

    private void LogCompletionResult(
        ClaimedPublicationNotification notification,
        PublicationNotificationCompletionResult completion,
        string operation)
    {
        if (!completion.Succeeded)
        {
            _logger.LogError(
                "Notification {NotificationId} {Operation} failed: {ErrorMessage}.",
                notification.NotificationId,
                operation,
                completion.ErrorMessage);
            return;
        }

        if (completion.Outcome == PublicationNotificationCompletionOutcome.OwnershipLost)
        {
            _logger.LogInformation(
                "Notification {NotificationId} {Operation} skipped because lease ownership was lost. ServiceType: {ServiceType}. MessageId: {MessageId}. LockOwner: {LockOwner}.",
                notification.NotificationId,
                operation,
                notification.ServiceType,
                notification.MessageId,
                _lockOwner);
        }
    }

    private static string SanitizeError(string? error)
    {
        const int maxLength = 2048;
        if (string.IsNullOrWhiteSpace(error))
        {
            return "Publication notification delivery failed.";
        }

        return error.Length <= maxLength ? error : error[..maxLength];
    }

    private static Task DelayAsync(int delaySeconds, CancellationToken cancellationToken)
    {
        return Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
    }
}
