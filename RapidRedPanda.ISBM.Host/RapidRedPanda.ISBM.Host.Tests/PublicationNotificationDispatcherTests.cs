using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Services;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class PublicationNotificationDispatcherTests
{
    [Fact]
    public async Task Dispatcher_DeliveredResult_MarksNotificationDelivered()
    {
        FakeRepository repository = new(Notification());
        PublicationNotificationDispatcher dispatcher = CreateDispatcher(
            repository,
            new FakeClient(PublicationNotificationDeliveryResult.Delivered(204)));

        await dispatcher.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => repository.DeliveredNotifications.Count == 1);
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.NotEmpty(repository.ClaimRequests);
        (Guid notificationId, string lockOwner) = Assert.Single(repository.DeliveredNotifications);
        Assert.Equal(repository.Notification.NotificationId, notificationId);
        Assert.Equal(repository.ClaimRequests[0].LockOwner, lockOwner);
        Assert.Empty(repository.RetryNotifications);
        Assert.Empty(repository.FailedNotifications);
    }

    [Fact]
    public async Task Dispatcher_RequestDeliveredResult_MarksNotificationDelivered()
    {
        FakeRepository repository = new(RequestNotification());
        FakeClient client = new(PublicationNotificationDeliveryResult.Delivered(204));
        PublicationNotificationDispatcher dispatcher = CreateDispatcher(repository, client);

        await dispatcher.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => repository.DeliveredNotifications.Count == 1);
        await dispatcher.StopAsync(CancellationToken.None);

        (Guid notificationId, _) = Assert.Single(repository.DeliveredNotifications);
        Assert.Equal(repository.Notification.NotificationId, notificationId);
        Assert.Equal(NotificationServiceTypes.Request, client.LastNotification?.ServiceType);
        Assert.Equal(repository.Notification.ProviderRequestSessionId, client.LastNotification?.ProviderRequestSessionId);
    }

    [Fact]
    public async Task Dispatcher_RetryableResult_SchedulesRetry()
    {
        FakeRepository repository = new(Notification(attemptCount: 1));
        PublicationNotificationDispatcher dispatcher = CreateDispatcher(
            repository,
            new FakeClient(PublicationNotificationDeliveryResult.RetryableFailure(503, "HTTP 503.")));

        await dispatcher.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => repository.RetryNotifications.Count == 1);
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.Single(repository.RetryNotifications);
        Assert.Empty(repository.FailedNotifications);
    }

    [Fact]
    public async Task Dispatcher_RetryableResultAtAttemptLimit_MarksFailed()
    {
        FakeRepository repository = new(Notification(attemptCount: 8));
        PublicationNotificationDispatcher dispatcher = CreateDispatcher(
            repository,
            new FakeClient(PublicationNotificationDeliveryResult.RetryableFailure(503, "HTTP 503.")));

        await dispatcher.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => repository.FailedNotifications.Count == 1);
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.Single(repository.FailedNotifications);
        Assert.Empty(repository.RetryNotifications);
    }

    [Fact]
    public async Task Dispatcher_IneligibleNotification_IsSuppressedWithoutHttpSend()
    {
        FakeRepository repository = new(Notification(isEligible: false, suppressionReason: PublicationNotificationSuppressionReasons.SessionClosed));
        FakeClient client = new(PublicationNotificationDeliveryResult.Delivered(204));
        PublicationNotificationDispatcher dispatcher = CreateDispatcher(repository, client);

        await dispatcher.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => repository.SuppressedNotifications.Count == 1);
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.Equal(0, client.SendCount);
        Assert.Single(repository.SuppressedNotifications);
    }

    [Fact]
    public async Task Dispatcher_ExpiredNotification_IsMarkedExpiredWithoutHttpSend()
    {
        FakeRepository repository = new(Notification(isDeliveryWindowExpired: true));
        FakeClient client = new(PublicationNotificationDeliveryResult.Delivered(204));
        PublicationNotificationDispatcher dispatcher = CreateDispatcher(repository, client);

        await dispatcher.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => repository.ExpiredNotifications.Count == 1);
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.Equal(0, client.SendCount);
        Assert.Single(repository.ExpiredNotifications);
    }

    [Fact]
    public async Task Dispatcher_FinalEligibilitySuppression_PreventsHttpSend()
    {
        FakeRepository repository = new(RequestNotification())
        {
            RecheckResult = PublicationNotificationEligibilityResult.Success(
                isEligible: false,
                PublicationNotificationSuppressionReasons.ProviderRequestSessionClosed,
                isDeliveryWindowExpired: false)
        };
        FakeClient client = new(PublicationNotificationDeliveryResult.Delivered(204));
        PublicationNotificationDispatcher dispatcher = CreateDispatcher(repository, client);

        await dispatcher.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => repository.SuppressedNotifications.Count == 1);
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.Equal(0, client.SendCount);
        Assert.Single(repository.RecheckRequests);
        Assert.Single(repository.SuppressedNotifications);
        Assert.Empty(repository.DeliveredNotifications);
    }

    [Fact]
    public async Task Dispatcher_FinalDeliveryWindowExpiry_IsExpiredWithoutHttpSend()
    {
        FakeRepository repository = new(RequestNotification())
        {
            RecheckResult = PublicationNotificationEligibilityResult.Success(
                isEligible: true,
                suppressionReason: null,
                isDeliveryWindowExpired: true)
        };
        FakeClient client = new(PublicationNotificationDeliveryResult.Delivered(204));
        PublicationNotificationDispatcher dispatcher = CreateDispatcher(repository, client);

        await dispatcher.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => repository.ExpiredNotifications.Count == 1);
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.Equal(0, client.SendCount);
        Assert.Single(repository.RecheckRequests);
        Assert.Single(repository.ExpiredNotifications);
        Assert.Empty(repository.DeliveredNotifications);
    }

    [Fact]
    public async Task Dispatcher_MalformedRequestNotification_IsFailedWithoutHttpSend()
    {
        FakeRepository repository = new(MalformedRequestNotification());
        FakeClient client = new(PublicationNotificationDeliveryResult.Delivered(204));
        PublicationNotificationDispatcher dispatcher = CreateDispatcher(repository, client);

        await dispatcher.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => repository.FailedNotifications.Count == 1);
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.Equal(0, client.SendCount);
        Assert.Single(repository.FailedNotifications);
    }

    [Fact]
    public async Task Dispatcher_OwnershipLostCompletion_DoesNotCrashLoop()
    {
        FakeRepository repository = new(
            Notification(),
            PublicationNotificationCompletionOutcome.OwnershipLost);
        PublicationNotificationDispatcher dispatcher = CreateDispatcher(
            repository,
            new FakeClient(PublicationNotificationDeliveryResult.Delivered(204)));

        await dispatcher.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => repository.DeliveredNotifications.Count == 1);
        await WaitUntilAsync(() => repository.ClaimRequests.Count >= 2);
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.Equal(PublicationNotificationCompletionOutcome.OwnershipLost, repository.LastCompletionOutcome);
    }

    [Fact]
    public async Task Dispatcher_Disabled_DoesNotClaimWork()
    {
        FakeRepository repository = new(Notification());
        FakeClient client = new(PublicationNotificationDeliveryResult.Delivered(204));
        PublicationNotificationDispatcher dispatcher = CreateDispatcher(
            repository,
            client,
            new PublicationNotificationOptions { Enabled = false });

        await dispatcher.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.Empty(repository.ClaimRequests);
        Assert.Equal(0, client.SendCount);
        Assert.Empty(repository.DeliveredNotifications);
        Assert.Empty(repository.RetryNotifications);
        Assert.Empty(repository.FailedNotifications);
        Assert.Empty(repository.SuppressedNotifications);
    }

    [Fact]
    public async Task Dispatcher_MixedPublicationAndRequestBatch_DeliversBothThroughSharedPipeline()
    {
        ClaimedPublicationNotification publication = Notification();
        ClaimedPublicationNotification request = RequestNotification();
        FakeRepository repository = new(publication, request);
        FakeClient client = new(PublicationNotificationDeliveryResult.Delivered(204));
        PublicationNotificationDispatcher dispatcher = CreateDispatcher(repository, client);

        await dispatcher.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => repository.DeliveredNotifications.Count == 2);
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.Equal(2, client.SendCount);
        Assert.Contains(client.SentNotifications, notification => notification.ServiceType == NotificationServiceTypes.Publication);
        Assert.Contains(client.SentNotifications, notification => notification.ServiceType == NotificationServiceTypes.Request);
        Assert.Contains(repository.DeliveredNotifications, delivered => delivered.NotificationId == publication.NotificationId);
        Assert.Contains(repository.DeliveredNotifications, delivered => delivered.NotificationId == request.NotificationId);
    }

    [Fact]
    public async Task Dispatcher_MalformedRequestRow_DoesNotBlockPublicationDelivery()
    {
        ClaimedPublicationNotification malformedRequest = MalformedRequestNotification();
        ClaimedPublicationNotification publication = Notification();
        FakeRepository repository = new(malformedRequest, publication);
        FakeClient client = new(PublicationNotificationDeliveryResult.Delivered(204));
        PublicationNotificationDispatcher dispatcher = CreateDispatcher(repository, client);

        await dispatcher.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => repository.FailedNotifications.Count == 1 && repository.DeliveredNotifications.Count == 1);
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.Single(repository.FailedNotifications);
        Assert.Single(repository.DeliveredNotifications);
        Assert.Equal(1, client.SendCount);
        Assert.Equal(NotificationServiceTypes.Publication, Assert.Single(client.SentNotifications).ServiceType);
    }

    [Fact]
    public async Task Dispatcher_MalformedResponseRow_IsFailedWithoutBlockingPublicationDelivery()
    {
        ClaimedPublicationNotification malformedResponse = MalformedResponseNotification();
        ClaimedPublicationNotification publication = Notification();
        FakeRepository repository = new(malformedResponse, publication);
        FakeClient client = new(PublicationNotificationDeliveryResult.Delivered(204));
        PublicationNotificationDispatcher dispatcher = CreateDispatcher(repository, client);

        await dispatcher.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => repository.FailedNotifications.Count == 1 && repository.DeliveredNotifications.Count == 1);
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.Single(repository.FailedNotifications);
        Assert.Single(repository.DeliveredNotifications);
        Assert.Equal(1, client.SendCount);
        Assert.Equal(NotificationServiceTypes.Publication, Assert.Single(client.SentNotifications).ServiceType);
    }

    private static PublicationNotificationDispatcher CreateDispatcher(
        FakeRepository repository,
        FakeClient client,
        PublicationNotificationOptions? options = null)
    {
        ServiceCollection services = new();
        services.AddSingleton<IPublicationNotificationRepository>(repository);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        return new PublicationNotificationDispatcher(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            client,
            Options.Create(options ?? new PublicationNotificationOptions
            {
                BatchSize = 20,
                MaxConcurrency = 4,
                IdlePollIntervalSeconds = 1,
                ErrorPollIntervalSeconds = 1,
                LeaseDurationSeconds = 60,
                HttpTimeoutSeconds = 10,
                MaximumAttempts = 8,
                MaximumDeliveryAgeMinutes = 1440,
                InitialRetryDelaySeconds = 1,
                MaximumRetryDelaySeconds = 2,
                JitterPercent = 0
            }),
            TimeProvider.System,
            NullLogger<PublicationNotificationDispatcher>.Instance);
    }

    private static ClaimedPublicationNotification Notification(
        int attemptCount = 1,
        bool isEligible = true,
        string? suppressionReason = null,
        bool isDeliveryWindowExpired = false)
    {
        return new ClaimedPublicationNotification(
            Guid.NewGuid(),
            NotificationServiceTypes.Publication,
            Guid.NewGuid(),
            RequestMessageId: null,
            Guid.NewGuid(),
            ProviderRequestSessionId: null,
            ConsumerRequestSessionId: null,
            "https://listener.example.com/isbm",
            ["Topic-A"],
            attemptCount,
            DateTime.UtcNow,
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(1),
            isEligible,
            suppressionReason,
            isDeliveryWindowExpired);
    }

    private static ClaimedPublicationNotification RequestNotification(
        Guid? providerRequestSessionId = null,
        int attemptCount = 1,
        bool isEligible = true,
        string? suppressionReason = null,
        bool isDeliveryWindowExpired = false)
    {
        return new ClaimedPublicationNotification(
            Guid.NewGuid(),
            NotificationServiceTypes.Request,
            Guid.NewGuid(),
            RequestMessageId: null,
            ConsumerSessionId: null,
            providerRequestSessionId ?? Guid.NewGuid(),
            ConsumerRequestSessionId: null,
            "https://listener.example.com/isbm",
            ["Topic-A"],
            attemptCount,
            DateTime.UtcNow,
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(1),
            isEligible,
            suppressionReason,
            isDeliveryWindowExpired);
    }

    private static ClaimedPublicationNotification MalformedResponseNotification()
    {
        return new ClaimedPublicationNotification(
            Guid.NewGuid(),
            NotificationServiceTypes.Response,
            Guid.NewGuid(),
            RequestMessageId: null,
            ConsumerSessionId: null,
            ProviderRequestSessionId: null,
            Guid.NewGuid(),
            "https://listener.example.com/isbm",
            [],
            1,
            DateTime.UtcNow,
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(1),
            IsEligible: true,
            SuppressionReason: null,
            IsDeliveryWindowExpired: false);
    }

    private static ClaimedPublicationNotification MalformedRequestNotification()
    {
        return new ClaimedPublicationNotification(
            Guid.NewGuid(),
            NotificationServiceTypes.Request,
            Guid.NewGuid(),
            RequestMessageId: null,
            ConsumerSessionId: null,
            ProviderRequestSessionId: null,
            ConsumerRequestSessionId: null,
            "https://listener.example.com/isbm",
            ["Topic-A"],
            1,
            DateTime.UtcNow,
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(1),
            IsEligible: true,
            SuppressionReason: null,
            IsDeliveryWindowExpired: false);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(25, timeout.Token);
        }
    }

    private sealed class FakeClient(PublicationNotificationDeliveryResult result) : IPublicationNotificationClient
    {
        public int SendCount { get; private set; }
        public ClaimedPublicationNotification? LastNotification { get; private set; }
        public List<ClaimedPublicationNotification> SentNotifications { get; } = [];

        public Task<PublicationNotificationDeliveryResult> SendAsync(
            ClaimedPublicationNotification notification,
            CancellationToken cancellationToken)
        {
            SendCount++;
            LastNotification = notification;
            SentNotifications.Add(notification);
            return Task.FromResult(result);
        }
    }

    private sealed class FakeRepository : IPublicationNotificationRepository
    {
        private bool _claimed;
        private readonly IReadOnlyList<ClaimedPublicationNotification> _notifications;
        private readonly PublicationNotificationCompletionOutcome _completionOutcome;

        public FakeRepository(
            ClaimedPublicationNotification notification,
            PublicationNotificationCompletionOutcome completionOutcome = PublicationNotificationCompletionOutcome.Delivered)
            : this([notification], completionOutcome)
        {
        }

        public FakeRepository(params ClaimedPublicationNotification[] notifications)
            : this(notifications, PublicationNotificationCompletionOutcome.Delivered)
        {
        }

        private FakeRepository(
            IReadOnlyList<ClaimedPublicationNotification> notifications,
            PublicationNotificationCompletionOutcome completionOutcome)
        {
            _notifications = notifications;
            _completionOutcome = completionOutcome;
            Notification = notifications[0];
        }

        public ClaimedPublicationNotification Notification { get; }
        public List<(string LockOwner, int BatchSize, TimeSpan LeaseDuration)> ClaimRequests { get; } = [];
        public List<(Guid NotificationId, string LockOwner)> DeliveredNotifications { get; } = [];
        public List<(Guid NotificationId, string LockOwner)> RetryNotifications { get; } = [];
        public List<(Guid NotificationId, string LockOwner)> FailedNotifications { get; } = [];
        public List<(Guid NotificationId, string LockOwner)> SuppressedNotifications { get; } = [];
        public List<(Guid NotificationId, string LockOwner)> ExpiredNotifications { get; } = [];
        public List<(Guid NotificationId, string LockOwner)> RecheckRequests { get; } = [];
        public PublicationNotificationEligibilityResult RecheckResult { get; init; } =
            PublicationNotificationEligibilityResult.Success(true, null, false);
        public PublicationNotificationCompletionOutcome? LastCompletionOutcome { get; private set; }

        public Task<PublicationNotificationClaimResult> ClaimDueAsync(
            string lockOwner,
            int batchSize,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            ClaimRequests.Add((lockOwner, batchSize, leaseDuration));
            if (_claimed)
            {
                return Task.FromResult(PublicationNotificationClaimResult.Success([]));
            }

            _claimed = true;
            return Task.FromResult(PublicationNotificationClaimResult.Success(_notifications));
        }

        public Task<PublicationNotificationEligibilityResult> RecheckClaimedEligibilityAsync(Guid notificationId, string lockOwner, CancellationToken cancellationToken)
        {
            RecheckRequests.Add((notificationId, lockOwner));
            return Task.FromResult(RecheckResult);
        }

        public Task<PublicationNotificationCompletionResult> MarkDeliveredAsync(Guid notificationId, string lockOwner, int statusCode, CancellationToken cancellationToken)
        {
            DeliveredNotifications.Add((notificationId, lockOwner));
            LastCompletionOutcome = _completionOutcome;
            return Task.FromResult(PublicationNotificationCompletionResult.Success(_completionOutcome));
        }

        public Task<PublicationNotificationCompletionResult> ScheduleRetryAsync(Guid notificationId, string lockOwner, TimeSpan retryDelay, int? statusCode, string error, CancellationToken cancellationToken)
        {
            RetryNotifications.Add((notificationId, lockOwner));
            LastCompletionOutcome = PublicationNotificationCompletionOutcome.RetryScheduled;
            return Task.FromResult(PublicationNotificationCompletionResult.Success(PublicationNotificationCompletionOutcome.RetryScheduled));
        }

        public Task<PublicationNotificationCompletionResult> CompleteRetryableFailureAsync(Guid notificationId, string lockOwner, int maximumAttempts, TimeSpan retryDelay, int? statusCode, string error, CancellationToken cancellationToken)
        {
            ClaimedPublicationNotification notification = _notifications.First(item => item.NotificationId == notificationId);
            PublicationNotificationCompletionOutcome outcome = notification.AttemptCount >= maximumAttempts
                ? PublicationNotificationCompletionOutcome.Failed
                : PublicationNotificationCompletionOutcome.RetryScheduled;

            if (outcome == PublicationNotificationCompletionOutcome.Failed)
            {
                FailedNotifications.Add((notificationId, lockOwner));
            }
            else
            {
                RetryNotifications.Add((notificationId, lockOwner));
            }

            LastCompletionOutcome = outcome;
            return Task.FromResult(PublicationNotificationCompletionResult.Success(outcome));
        }

        public Task<PublicationNotificationCompletionResult> MarkFailedAsync(Guid notificationId, string lockOwner, int? statusCode, string error, CancellationToken cancellationToken)
        {
            FailedNotifications.Add((notificationId, lockOwner));
            LastCompletionOutcome = PublicationNotificationCompletionOutcome.Failed;
            return Task.FromResult(PublicationNotificationCompletionResult.Success(PublicationNotificationCompletionOutcome.Failed));
        }

        public Task<PublicationNotificationCompletionResult> MarkSuppressedAsync(Guid notificationId, string lockOwner, string resolutionReason, CancellationToken cancellationToken)
        {
            SuppressedNotifications.Add((notificationId, lockOwner));
            LastCompletionOutcome = PublicationNotificationCompletionOutcome.Suppressed;
            return Task.FromResult(PublicationNotificationCompletionResult.Success(PublicationNotificationCompletionOutcome.Suppressed));
        }

        public Task<PublicationNotificationCompletionResult> MarkExpiredAsync(Guid notificationId, string lockOwner, CancellationToken cancellationToken)
        {
            ExpiredNotifications.Add((notificationId, lockOwner));
            LastCompletionOutcome = PublicationNotificationCompletionOutcome.Expired;
            return Task.FromResult(PublicationNotificationCompletionResult.Success(PublicationNotificationCompletionOutcome.Expired));
        }

        public Task<PublicationNotificationSuppressionResult> SuppressRequestNotificationsForSessionAsync(Guid providerRequestSessionId, DateTime suppressedUtc, CancellationToken cancellationToken)
        {
            return Task.FromResult(PublicationNotificationSuppressionResult.Success(0));
        }

        public Task<PublicationNotificationSuppressionResult> SuppressRequestNotificationsForMessageAsync(Guid requestMessageId, DateTime suppressedUtc, CancellationToken cancellationToken)
        {
            return Task.FromResult(PublicationNotificationSuppressionResult.Success(0));
        }

        public Task<PublicationNotificationSuppressionResult> SuppressResponseNotificationsForConsumerSessionAsync(Guid consumerRequestSessionId, DateTime suppressedUtc, CancellationToken cancellationToken)
        {
            return Task.FromResult(PublicationNotificationSuppressionResult.Success(0));
        }

        public Task<PublicationNotificationSuppressionResult> SuppressResponseNotificationsForRequestAsync(Guid requestMessageId, DateTime suppressedUtc, CancellationToken cancellationToken)
        {
            return Task.FromResult(PublicationNotificationSuppressionResult.Success(0));
        }

        public Task<PublicationNotificationSuppressionResult> SuppressResponseNotificationForResponseAsync(Guid responseMessageId, Guid consumerRequestSessionId, DateTime suppressedUtc, CancellationToken cancellationToken)
        {
            return Task.FromResult(PublicationNotificationSuppressionResult.Success(0));
        }

        public Task<PublicationNotificationQueryResult> GetPendingPublicationNotificationsAsync(int maxCount, CancellationToken cancellationToken)
        {
            return Task.FromResult(PublicationNotificationQueryResult.Success([]));
        }
    }
}
