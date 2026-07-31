using Microsoft.Data.SqlClient;
using System.Data;

namespace RapidRedPanda.ISBM.Host.Data;

public sealed class SqlPublicationNotificationRepository : IPublicationNotificationRepository
{
    private const string ConnectionStringName = "ISBMHost";
    private const int ErrorMaxLength = 2048;

    private readonly string _connectionString;
    private readonly ILogger<SqlPublicationNotificationRepository> _logger;

    public SqlPublicationNotificationRepository(
        IConfiguration configuration,
        ILogger<SqlPublicationNotificationRepository> logger)
    {
        _connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
        _logger = logger;
    }

    public async Task<PublicationNotificationClaimResult> ClaimDueAsync(
        string lockOwner,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (batchSize <= 0)
        {
            return PublicationNotificationClaimResult.Success([]);
        }

        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                const string claimSql = """
                    declare @SqlUtcNow datetime2(7) = sysutcdatetime();

                    update notification
                       set State = @ExpiredState,
                           ResolvedUtc = @SqlUtcNow,
                           ResolutionReason = @DeliveryWindowExpiredReason,
                           NextAttemptUtc = null,
                           LockOwner = null,
                           LockedUntilUtc = null
                     from dbo.PublicationNotifications notification with (updlock, readpast, rowlock)
                     where notification.DeliveryDeadlineUtc <= @SqlUtcNow
                       and
                       (
                          notification.State in (@PendingState, @RetryScheduledState)
                          or
                          (
                              notification.State = @SendingState
                              and notification.LockedUntilUtc < @SqlUtcNow
                          )
                      );

                    declare @Claimed table
                    (
                         PublicationNotificationId uniqueidentifier not null,
                         NotificationServiceType nvarchar(32) not null,
                         MessageId uniqueidentifier not null,
                         ConsumerSessionId uniqueidentifier null,
                         ProviderRequestSessionId uniqueidentifier null,
                         ListenerUrl nvarchar(2048) not null,
                        AttemptCount int not null,
                        NextAttemptUtc datetime2(7) null,
                        CreatedUtc datetime2(7) not null,
                        DeliveryDeadlineUtc datetime2(7) null
                    );

                    ;with due as
                    (
                        select top (@BatchSize)
                            notification.PublicationNotificationId
                         from dbo.PublicationNotifications notification with (updlock, readpast, rowlock)
                         where
                         (
                            (
                                notification.State in (@PendingState, @RetryScheduledState)
                                and (notification.NextAttemptUtc is null or notification.NextAttemptUtc <= @SqlUtcNow)
                                and (notification.LockedUntilUtc is null or notification.LockedUntilUtc < @SqlUtcNow)
                                and (notification.DeliveryDeadlineUtc is null or notification.DeliveryDeadlineUtc > @SqlUtcNow)
                            )
                            or
                            (
                                notification.State = @SendingState
                                and notification.LockedUntilUtc < @SqlUtcNow
                                and (notification.DeliveryDeadlineUtc is null or notification.DeliveryDeadlineUtc > @SqlUtcNow)
                            )
                        )
                        order by notification.NextAttemptUtc, notification.CreatedUtc, notification.PublicationNotificationId
                    )
                    update notification
                       set State = @SendingState,
                           LockOwner = @LockOwner,
                           LockedUntilUtc = dateadd(second, @LeaseSeconds, @SqlUtcNow),
                           LastAttemptUtc = @SqlUtcNow,
                           AttemptCount = AttemptCount + 1
                    output
                         inserted.PublicationNotificationId,
                         inserted.NotificationServiceType,
                         inserted.MessageId,
                         inserted.ConsumerSessionId,
                         inserted.ProviderRequestSessionId,
                         inserted.ListenerUrl,
                        inserted.AttemptCount,
                        inserted.NextAttemptUtc,
                        inserted.CreatedUtc,
                        inserted.DeliveryDeadlineUtc
                    into @Claimed
                    from dbo.PublicationNotifications notification
                    inner join due
                            on due.PublicationNotificationId = notification.PublicationNotificationId;

                    select
                         claimed.PublicationNotificationId,
                         claimed.NotificationServiceType,
                         claimed.MessageId,
                         claimed.ConsumerSessionId,
                         claimed.ProviderRequestSessionId,
                         claimed.ListenerUrl,
                        claimed.AttemptCount,
                        claimed.NextAttemptUtc,
                        claimed.CreatedUtc,
                        claimed.DeliveryDeadlineUtc,
                        coalesce(publicationSession.SessionType, requestSession.SessionType),
                        coalesce(publicationSession.SessionState, requestSession.SessionState),
                        publicationSession.ListenerUrl,
                        recipient.State,
                        publication.ExpiredUtc,
                        publication.EffectiveExpiryUtc,
                        request.MessageId,
                        request.ExpiredUtc,
                        request.EffectiveExpiryUtc,
                        @SqlUtcNow
                    from @Claimed claimed
                    left join dbo.ConsumerSubscriptionSessions publicationSession
                           on publicationSession.SessionId = claimed.ConsumerSessionId
                    left join dbo.ConsumerSubscriptionSessions requestSession
                           on requestSession.SessionId = claimed.ProviderRequestSessionId
                    left join dbo.ProviderPublicationRecipients recipient
                           on recipient.MessageId = claimed.MessageId
                          and recipient.ConsumerSessionId = claimed.ConsumerSessionId
                    left join dbo.ProviderPublications publication
                           on publication.MessageId = claimed.MessageId
                    left join dbo.ConsumerRequests request
                           on request.MessageId = claimed.MessageId
                    order by claimed.CreatedUtc, claimed.PublicationNotificationId;
                    """;

                List<ClaimedRow> rows = [];
                await using (SqlCommand command = new(claimSql, connection, transaction))
                {
                    command.Parameters.Add("@BatchSize", SqlDbType.Int).Value = batchSize;
                    command.Parameters.Add("@LeaseSeconds", SqlDbType.Int).Value = Math.Max(1, (int)Math.Ceiling(leaseDuration.TotalSeconds));
                    command.Parameters.Add("@LockOwner", SqlDbType.NVarChar, 128).Value = lockOwner;
                    command.Parameters.Add("@PendingState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.Pending;
                    command.Parameters.Add("@RetryScheduledState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.RetryScheduled;
                    command.Parameters.Add("@SendingState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.Sending;
                    command.Parameters.Add("@ExpiredState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.Expired;
                    command.Parameters.Add("@DeliveryWindowExpiredReason", SqlDbType.NVarChar, 64).Value =
                        PublicationNotificationSuppressionReasons.DeliveryWindowExpired;

                    await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        rows.Add(new ClaimedRow(
                            reader.GetGuid(0),
                            reader.GetString(1),
                            reader.GetGuid(2),
                            reader.IsDBNull(3) ? null : reader.GetGuid(3),
                            reader.IsDBNull(4) ? null : reader.GetGuid(4),
                            reader.GetString(5),
                            reader.GetInt32(6),
                            reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                            reader.GetDateTime(8),
                            reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                            reader.IsDBNull(10) ? null : reader.GetString(10),
                            reader.IsDBNull(11) ? null : reader.GetString(11),
                            reader.IsDBNull(12) ? null : reader.GetString(12),
                            reader.IsDBNull(13) ? null : reader.GetString(13),
                            reader.IsDBNull(14) ? null : reader.GetDateTime(14),
                            reader.IsDBNull(15) ? null : reader.GetDateTime(15),
                            reader.IsDBNull(16) ? null : reader.GetGuid(16),
                            reader.IsDBNull(17) ? null : reader.GetDateTime(17),
                            reader.IsDBNull(18) ? null : reader.GetDateTime(18),
                            reader.GetDateTime(19)));
                    }
                }

                List<ClaimedPublicationNotification> notifications = [];
                foreach (ClaimedRow row in rows)
                {
                    IReadOnlyList<string> topics = await ReadTopicsAsync(
                        connection,
                        transaction,
                        row.NotificationId,
                        cancellationToken);

                    string? suppressionReason = GetSuppressionReason(row);
                    bool isDeliveryWindowExpired = IsDeliveryWindowExpired(row);
                    notifications.Add(new ClaimedPublicationNotification(
                        row.NotificationId,
                        row.ServiceType,
                        row.MessageId,
                        row.ConsumerSessionId,
                        row.ProviderRequestSessionId,
                        row.ListenerUrl,
                        topics,
                        row.AttemptCount,
                        row.NextAttemptUtc,
                        row.CreatedUtc,
                        row.DeliveryDeadlineUtc,
                        suppressionReason is null,
                        suppressionReason,
                        isDeliveryWindowExpired));
                }

                await transaction.CommitAsync(cancellationToken);
                return PublicationNotificationClaimResult.Success(notifications);
            }
            catch
            {
                await RollbackQuietlyAsync(transaction, cancellationToken);
                throw;
            }
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while claiming publication notifications.");
            return PublicationNotificationClaimResult.Failure(
                ProviderPublicationRepositoryFailureKind.DatabaseFailure,
                "Database failure while claiming publication notifications.");
        }
    }

    public Task<PublicationNotificationCompletionResult> MarkDeliveredAsync(
        Guid notificationId,
        string lockOwner,
        int statusCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            declare @SqlUtcNow datetime2(7) = sysutcdatetime();

            update dbo.PublicationNotifications
               set State = @DeliveredState,
                   ResolvedUtc = @SqlUtcNow,
                   LastStatusCode = @StatusCode,
                   LastError = null,
                   LockOwner = null,
                   LockedUntilUtc = null
             where PublicationNotificationId = @NotificationId
               and State = @SendingState
               and LockOwner = @LockOwner
               and LockedUntilUtc >= @SqlUtcNow;
            """;

        return ExecuteStateUpdateAsync(
            notificationId,
            lockOwner,
            sql,
            PublicationNotificationCompletionOutcome.Delivered,
            command =>
            {
                command.Parameters.Add("@DeliveredState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.Delivered;
                command.Parameters.Add("@SendingState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.Sending;
                command.Parameters.Add("@StatusCode", SqlDbType.Int).Value = statusCode;
            },
            cancellationToken);
    }

    public Task<PublicationNotificationCompletionResult> ScheduleRetryAsync(
        Guid notificationId,
        string lockOwner,
        TimeSpan retryDelay,
        int? statusCode,
        string error,
        CancellationToken cancellationToken)
    {
        const string sql = """
            declare @SqlUtcNow datetime2(7) = sysutcdatetime();
            declare @Outcome nvarchar(32) = N'OwnershipLost';
            declare @ResolutionReason nvarchar(64) = null;

            if exists
            (
                select 1
                from dbo.PublicationNotifications notification
                where notification.PublicationNotificationId = @NotificationId
                  and notification.State = @SendingState
                  and notification.LockOwner = @LockOwner
                  and notification.LockedUntilUtc >= @SqlUtcNow
            )
            begin
                select @ResolutionReason =
                    case
                        when notification.NotificationServiceType = @PublicationServiceType
                             and (notification.ConsumerSessionId is null or notification.ProviderRequestSessionId is not null) then @InvalidNotificationShapeReason
                        when notification.NotificationServiceType = @PublicationServiceType
                             and (publicationSession.SessionState is null or publicationSession.SessionState <> @OpenSessionState) then @SessionClosedReason
                        when notification.NotificationServiceType = @PublicationServiceType
                             and (publicationSession.ListenerUrl is null or ltrim(rtrim(publicationSession.ListenerUrl)) = N'') then @ListenerMissingReason
                        when notification.NotificationServiceType = @PublicationServiceType
                             and (recipient.State is null or recipient.State <> @RecipientPendingState) then @RecipientNotPendingReason
                        when notification.NotificationServiceType = @PublicationServiceType
                             and (publication.ExpiredUtc is not null or publication.EffectiveExpiryUtc <= @SqlUtcNow) then @PublicationExpiredReason
                        when notification.NotificationServiceType = @RequestServiceType
                             and (notification.ProviderRequestSessionId is null or notification.ConsumerSessionId is not null) then @InvalidNotificationShapeReason
                        when notification.NotificationServiceType = @RequestServiceType
                             and (requestSession.SessionId is null or requestSession.SessionType <> @ProviderRequestSessionType or requestSession.SessionState <> @OpenSessionState) then @SessionClosedReason
                        when notification.NotificationServiceType = @RequestServiceType
                             and (notification.ListenerUrl is null or ltrim(rtrim(notification.ListenerUrl)) = N'') then @ListenerMissingReason
                        when notification.NotificationServiceType = @RequestServiceType
                             and request.MessageId is null then @RequestMissingReason
                        when notification.NotificationServiceType = @RequestServiceType
                             and (request.ExpiredUtc is not null or request.EffectiveExpiryUtc <= @SqlUtcNow) then @RequestExpiredReason
                        when notification.NotificationServiceType not in (@PublicationServiceType, @RequestServiceType) then @InvalidNotificationShapeReason
                        else null
                    end
                from dbo.PublicationNotifications notification
                left join dbo.ConsumerSubscriptionSessions publicationSession
                       on publicationSession.SessionId = notification.ConsumerSessionId
                left join dbo.ConsumerSubscriptionSessions requestSession
                       on requestSession.SessionId = notification.ProviderRequestSessionId
                left join dbo.ProviderPublicationRecipients recipient
                       on recipient.MessageId = notification.MessageId
                      and recipient.ConsumerSessionId = notification.ConsumerSessionId
                left join dbo.ProviderPublications publication
                       on publication.MessageId = notification.MessageId
                left join dbo.ConsumerRequests request
                       on request.MessageId = notification.MessageId
                where notification.PublicationNotificationId = @NotificationId;

                if @ResolutionReason is not null
                begin
                    update dbo.PublicationNotifications
                       set State = @SuppressedState,
                           ResolvedUtc = @SqlUtcNow,
                           ResolutionReason = @ResolutionReason,
                           LastStatusCode = @StatusCode,
                           LastError = null,
                           NextAttemptUtc = null,
                           LockOwner = null,
                           LockedUntilUtc = null
                     where PublicationNotificationId = @NotificationId
                       and State = @SendingState
                       and LockOwner = @LockOwner
                       and LockedUntilUtc >= @SqlUtcNow;

                    if @@rowcount = 0
                        set @Outcome = N'OwnershipLost';
                    else
                        set @Outcome = N'Suppressed';
                end
                else if exists
                (
                    select 1
                    from dbo.PublicationNotifications notification
                    where notification.PublicationNotificationId = @NotificationId
                      and notification.DeliveryDeadlineUtc <= @SqlUtcNow
                )
                begin
                    update dbo.PublicationNotifications
                       set State = @ExpiredState,
                           ResolvedUtc = @SqlUtcNow,
                           ResolutionReason = @DeliveryWindowExpiredReason,
                           LastStatusCode = @StatusCode,
                           LastError = @LastError,
                           NextAttemptUtc = null,
                           LockOwner = null,
                           LockedUntilUtc = null
                     where PublicationNotificationId = @NotificationId
                       and State = @SendingState
                       and LockOwner = @LockOwner
                       and LockedUntilUtc >= @SqlUtcNow;

                    if @@rowcount = 0
                        set @Outcome = N'OwnershipLost';
                    else
                    begin
                        set @Outcome = N'Expired';
                        set @ResolutionReason = @DeliveryWindowExpiredReason;
                    end
                end
                else if exists
                (
                    select 1
                    from dbo.PublicationNotifications notification
                    where notification.PublicationNotificationId = @NotificationId
                      and notification.DeliveryDeadlineUtc <= dateadd(second, @RetryDelaySeconds, @SqlUtcNow)
                )
                begin
                    update dbo.PublicationNotifications
                       set State = @ExpiredState,
                           ResolvedUtc = @SqlUtcNow,
                           ResolutionReason = @DeliveryWindowExpiredReason,
                           LastStatusCode = @StatusCode,
                           LastError = @LastError,
                           NextAttemptUtc = null,
                           LockOwner = null,
                           LockedUntilUtc = null
                     where PublicationNotificationId = @NotificationId
                       and State = @SendingState
                       and LockOwner = @LockOwner
                       and LockedUntilUtc >= @SqlUtcNow;

                    if @@rowcount = 0
                        set @Outcome = N'OwnershipLost';
                    else
                    begin
                        set @Outcome = N'Expired';
                        set @ResolutionReason = @DeliveryWindowExpiredReason;
                    end
                end
                else
                begin
                    update dbo.PublicationNotifications
                       set State = @RetryScheduledState,
                           NextAttemptUtc = dateadd(second, @RetryDelaySeconds, @SqlUtcNow),
                           LastAttemptUtc = @SqlUtcNow,
                           LastStatusCode = @StatusCode,
                           LastError = @LastError,
                           LockOwner = null,
                           LockedUntilUtc = null
                     where PublicationNotificationId = @NotificationId
                       and State = @SendingState
                       and LockOwner = @LockOwner
                       and LockedUntilUtc >= @SqlUtcNow;

                    if @@rowcount = 0
                        set @Outcome = N'OwnershipLost';
                    else
                        set @Outcome = N'RetryScheduled';
                end
            end

            select @Outcome, @ResolutionReason;
            """;

        return ExecuteCompletionDecisionAsync(
            notificationId,
            lockOwner,
            sql,
            command =>
            {
                AddRetryDecisionParameters(command, statusCode, error);
                command.Parameters.Add("@RetryScheduledState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.RetryScheduled;
                command.Parameters.Add("@RetryDelaySeconds", SqlDbType.Int).Value = GetDelaySeconds(retryDelay);
            },
            cancellationToken);
    }

    public Task<PublicationNotificationCompletionResult> CompleteRetryableFailureAsync(
        Guid notificationId,
        string lockOwner,
        int maximumAttempts,
        TimeSpan retryDelay,
        int? statusCode,
        string error,
        CancellationToken cancellationToken)
    {
        const string sql = """
            declare @SqlUtcNow datetime2(7) = sysutcdatetime();
            declare @Outcome nvarchar(32) = N'OwnershipLost';
            declare @ResolutionReason nvarchar(64) = null;

            if exists
            (
                select 1
                from dbo.PublicationNotifications notification
                where notification.PublicationNotificationId = @NotificationId
                  and notification.State = @SendingState
                  and notification.LockOwner = @LockOwner
                  and notification.LockedUntilUtc >= @SqlUtcNow
            )
            begin
                select @ResolutionReason =
                    case
                        when notification.NotificationServiceType = @PublicationServiceType
                             and (notification.ConsumerSessionId is null or notification.ProviderRequestSessionId is not null) then @InvalidNotificationShapeReason
                        when notification.NotificationServiceType = @PublicationServiceType
                             and (publicationSession.SessionState is null or publicationSession.SessionState <> @OpenSessionState) then @SessionClosedReason
                        when notification.NotificationServiceType = @PublicationServiceType
                             and (publicationSession.ListenerUrl is null or ltrim(rtrim(publicationSession.ListenerUrl)) = N'') then @ListenerMissingReason
                        when notification.NotificationServiceType = @PublicationServiceType
                             and (recipient.State is null or recipient.State <> @RecipientPendingState) then @RecipientNotPendingReason
                        when notification.NotificationServiceType = @PublicationServiceType
                             and (publication.ExpiredUtc is not null or publication.EffectiveExpiryUtc <= @SqlUtcNow) then @PublicationExpiredReason
                        when notification.NotificationServiceType = @RequestServiceType
                             and (notification.ProviderRequestSessionId is null or notification.ConsumerSessionId is not null) then @InvalidNotificationShapeReason
                        when notification.NotificationServiceType = @RequestServiceType
                             and (requestSession.SessionId is null or requestSession.SessionType <> @ProviderRequestSessionType or requestSession.SessionState <> @OpenSessionState) then @SessionClosedReason
                        when notification.NotificationServiceType = @RequestServiceType
                             and (notification.ListenerUrl is null or ltrim(rtrim(notification.ListenerUrl)) = N'') then @ListenerMissingReason
                        when notification.NotificationServiceType = @RequestServiceType
                             and request.MessageId is null then @RequestMissingReason
                        when notification.NotificationServiceType = @RequestServiceType
                             and (request.ExpiredUtc is not null or request.EffectiveExpiryUtc <= @SqlUtcNow) then @RequestExpiredReason
                        when notification.NotificationServiceType not in (@PublicationServiceType, @RequestServiceType) then @InvalidNotificationShapeReason
                        else null
                    end
                from dbo.PublicationNotifications notification
                left join dbo.ConsumerSubscriptionSessions publicationSession
                       on publicationSession.SessionId = notification.ConsumerSessionId
                left join dbo.ConsumerSubscriptionSessions requestSession
                       on requestSession.SessionId = notification.ProviderRequestSessionId
                left join dbo.ProviderPublicationRecipients recipient
                       on recipient.MessageId = notification.MessageId
                      and recipient.ConsumerSessionId = notification.ConsumerSessionId
                left join dbo.ProviderPublications publication
                       on publication.MessageId = notification.MessageId
                left join dbo.ConsumerRequests request
                       on request.MessageId = notification.MessageId
                where notification.PublicationNotificationId = @NotificationId;

                if @ResolutionReason is not null
                begin
                    update dbo.PublicationNotifications
                       set State = @SuppressedState,
                           ResolvedUtc = @SqlUtcNow,
                           ResolutionReason = @ResolutionReason,
                           LastStatusCode = @StatusCode,
                           LastError = null,
                           NextAttemptUtc = null,
                           LockOwner = null,
                           LockedUntilUtc = null
                     where PublicationNotificationId = @NotificationId
                       and State = @SendingState
                       and LockOwner = @LockOwner
                       and LockedUntilUtc >= @SqlUtcNow;

                    if @@rowcount = 0
                        set @Outcome = N'OwnershipLost';
                    else
                        set @Outcome = N'Suppressed';
                end
                else if exists
                (
                    select 1
                    from dbo.PublicationNotifications notification
                    where notification.PublicationNotificationId = @NotificationId
                      and notification.DeliveryDeadlineUtc <= @SqlUtcNow
                )
                begin
                    update dbo.PublicationNotifications
                       set State = @ExpiredState,
                           ResolvedUtc = @SqlUtcNow,
                           ResolutionReason = @DeliveryWindowExpiredReason,
                           LastStatusCode = @StatusCode,
                           LastError = @LastError,
                           NextAttemptUtc = null,
                           LockOwner = null,
                           LockedUntilUtc = null
                     where PublicationNotificationId = @NotificationId
                       and State = @SendingState
                       and LockOwner = @LockOwner
                       and LockedUntilUtc >= @SqlUtcNow;

                    if @@rowcount = 0
                        set @Outcome = N'OwnershipLost';
                    else
                    begin
                        set @Outcome = N'Expired';
                        set @ResolutionReason = @DeliveryWindowExpiredReason;
                    end
                end
                else if exists
                (
                    select 1
                    from dbo.PublicationNotifications notification
                    where notification.PublicationNotificationId = @NotificationId
                      and notification.AttemptCount >= @MaximumAttempts
                )
                begin
                    update dbo.PublicationNotifications
                       set State = @FailedState,
                           ResolvedUtc = @SqlUtcNow,
                           LastAttemptUtc = @SqlUtcNow,
                           LastStatusCode = @StatusCode,
                           LastError = @LastError,
                           LockOwner = null,
                           LockedUntilUtc = null
                     where PublicationNotificationId = @NotificationId
                       and State = @SendingState
                       and LockOwner = @LockOwner
                       and LockedUntilUtc >= @SqlUtcNow;

                    if @@rowcount = 0
                        set @Outcome = N'OwnershipLost';
                    else
                        set @Outcome = N'Failed';
                end
                else if exists
                (
                    select 1
                    from dbo.PublicationNotifications notification
                    where notification.PublicationNotificationId = @NotificationId
                      and notification.DeliveryDeadlineUtc <= dateadd(second, @RetryDelaySeconds, @SqlUtcNow)
                )
                begin
                    update dbo.PublicationNotifications
                       set State = @ExpiredState,
                           ResolvedUtc = @SqlUtcNow,
                           ResolutionReason = @DeliveryWindowExpiredReason,
                           LastStatusCode = @StatusCode,
                           LastError = @LastError,
                           NextAttemptUtc = null,
                           LockOwner = null,
                           LockedUntilUtc = null
                     where PublicationNotificationId = @NotificationId
                       and State = @SendingState
                       and LockOwner = @LockOwner
                       and LockedUntilUtc >= @SqlUtcNow;

                    if @@rowcount = 0
                        set @Outcome = N'OwnershipLost';
                    else
                    begin
                        set @Outcome = N'Expired';
                        set @ResolutionReason = @DeliveryWindowExpiredReason;
                    end
                end
                else
                begin
                    update dbo.PublicationNotifications
                       set State = @RetryScheduledState,
                           NextAttemptUtc = dateadd(second, @RetryDelaySeconds, @SqlUtcNow),
                           LastAttemptUtc = @SqlUtcNow,
                           LastStatusCode = @StatusCode,
                           LastError = @LastError,
                           LockOwner = null,
                           LockedUntilUtc = null
                     where PublicationNotificationId = @NotificationId
                       and State = @SendingState
                       and LockOwner = @LockOwner
                       and LockedUntilUtc >= @SqlUtcNow;

                    if @@rowcount = 0
                        set @Outcome = N'OwnershipLost';
                    else
                        set @Outcome = N'RetryScheduled';
                end
            end

            select @Outcome, @ResolutionReason;
            """;

        return ExecuteCompletionDecisionAsync(
            notificationId,
            lockOwner,
            sql,
            command =>
            {
                AddRetryDecisionParameters(command, statusCode, error);
                command.Parameters.Add("@MaximumAttempts", SqlDbType.Int).Value = maximumAttempts;
                command.Parameters.Add("@FailedState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.Failed;
                command.Parameters.Add("@RetryScheduledState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.RetryScheduled;
                command.Parameters.Add("@RetryDelaySeconds", SqlDbType.Int).Value = GetDelaySeconds(retryDelay);
            },
            cancellationToken);
    }

    public Task<PublicationNotificationCompletionResult> MarkFailedAsync(
        Guid notificationId,
        string lockOwner,
        int? statusCode,
        string error,
        CancellationToken cancellationToken)
    {
        const string sql = """
            declare @SqlUtcNow datetime2(7) = sysutcdatetime();

            update dbo.PublicationNotifications
               set State = @FailedState,
                   ResolvedUtc = @SqlUtcNow,
                   LastAttemptUtc = @SqlUtcNow,
                   LastStatusCode = @StatusCode,
                   LastError = @LastError,
                   LockOwner = null,
                   LockedUntilUtc = null
             where PublicationNotificationId = @NotificationId
               and State = @SendingState
               and LockOwner = @LockOwner
               and LockedUntilUtc >= @SqlUtcNow;
            """;

        return ExecuteStateUpdateAsync(
            notificationId,
            lockOwner,
            sql,
            PublicationNotificationCompletionOutcome.Failed,
            command =>
            {
                command.Parameters.Add("@FailedState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.Failed;
                command.Parameters.Add("@SendingState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.Sending;
                command.Parameters.Add("@StatusCode", SqlDbType.Int).Value = (object?)statusCode ?? DBNull.Value;
                command.Parameters.Add("@LastError", SqlDbType.NVarChar, ErrorMaxLength).Value = TruncateError(error);
            },
            cancellationToken);
    }

    public Task<PublicationNotificationCompletionResult> MarkSuppressedAsync(
        Guid notificationId,
        string lockOwner,
        string resolutionReason,
        CancellationToken cancellationToken)
    {
        const string sql = """
            declare @SqlUtcNow datetime2(7) = sysutcdatetime();

            update dbo.PublicationNotifications
               set State = @SuppressedState,
                   ResolvedUtc = @SqlUtcNow,
                   ResolutionReason = @ResolutionReason,
                   LastError = null,
                   NextAttemptUtc = null,
                   LockOwner = null,
                   LockedUntilUtc = null
             where PublicationNotificationId = @NotificationId
               and State = @SendingState
               and LockOwner = @LockOwner
               and LockedUntilUtc >= @SqlUtcNow;
            """;

        return ExecuteStateUpdateAsync(
            notificationId,
            lockOwner,
            sql,
            PublicationNotificationCompletionOutcome.Suppressed,
            command =>
            {
                command.Parameters.Add("@SuppressedState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.Suppressed;
                command.Parameters.Add("@SendingState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.Sending;
                command.Parameters.Add("@ResolutionReason", SqlDbType.NVarChar, 64).Value = resolutionReason;
            },
            cancellationToken);
    }

    public Task<PublicationNotificationCompletionResult> MarkExpiredAsync(
        Guid notificationId,
        string lockOwner,
        CancellationToken cancellationToken)
    {
        const string sql = """
            declare @SqlUtcNow datetime2(7) = sysutcdatetime();

            update dbo.PublicationNotifications
               set State = @ExpiredState,
                   ResolvedUtc = @SqlUtcNow,
                   ResolutionReason = @DeliveryWindowExpiredReason,
                   NextAttemptUtc = null,
                   LockOwner = null,
                   LockedUntilUtc = null
             where PublicationNotificationId = @NotificationId
               and State = @SendingState
               and LockOwner = @LockOwner
               and LockedUntilUtc >= @SqlUtcNow;
            """;

        return ExecuteStateUpdateAsync(
            notificationId,
            lockOwner,
            sql,
            PublicationNotificationCompletionOutcome.Expired,
            command =>
            {
                command.Parameters.Add("@ExpiredState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.Expired;
                command.Parameters.Add("@SendingState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.Sending;
                command.Parameters.Add("@DeliveryWindowExpiredReason", SqlDbType.NVarChar, 64).Value =
                    PublicationNotificationSuppressionReasons.DeliveryWindowExpired;
            },
            cancellationToken);
    }

    public async Task<PublicationNotificationQueryResult> GetPendingPublicationNotificationsAsync(
        int maxCount,
        CancellationToken cancellationToken)
    {
        if (maxCount <= 0)
        {
            return PublicationNotificationQueryResult.Success([]);
        }

        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string notificationSql = """
                 select top (@MaxCount)
                     PublicationNotificationId,
                     NotificationServiceType,
                     MessageId,
                     ConsumerSessionId,
                     ProviderRequestSessionId,
                     ListenerUrl,
                    State,
                    AttemptCount,
                    NextAttemptUtc,
                    CreatedUtc,
                    DeliveryDeadlineUtc,
                    ResolvedUtc
                 from dbo.PublicationNotifications
                 where NotificationServiceType = @PublicationServiceType
                   and State in (@PendingState, @RetryScheduledState)
                 order by CreatedUtc, PublicationNotificationId;
                """;

            List<PublicationNotificationData> notifications = [];
            await using (SqlCommand command = new(notificationSql, connection))
            {
                command.Parameters.Add("@MaxCount", SqlDbType.Int).Value = maxCount;
                command.Parameters.Add("@PublicationServiceType", SqlDbType.NVarChar, 32).Value = NotificationServiceTypes.Publication;
                command.Parameters.Add("@PendingState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.Pending;
                command.Parameters.Add("@RetryScheduledState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.RetryScheduled;

                await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    notifications.Add(new PublicationNotificationData(
                        reader.GetGuid(0),
                        reader.GetString(1),
                        reader.GetGuid(2),
                        reader.IsDBNull(3) ? null : reader.GetGuid(3),
                        reader.IsDBNull(4) ? null : reader.GetGuid(4),
                        reader.GetString(5),
                        [],
                        reader.GetString(6),
                        reader.GetInt32(7),
                        reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                        reader.GetDateTime(9),
                        reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                        reader.IsDBNull(11) ? null : reader.GetDateTime(11)));
                }
            }

            for (int index = 0; index < notifications.Count; index++)
            {
                IReadOnlyList<string> topics = await ReadTopicsAsync(
                    connection,
                    transaction: null,
                    notifications[index].PublicationNotificationId,
                    cancellationToken);
                notifications[index] = notifications[index] with { Topics = topics };
            }

            return PublicationNotificationQueryResult.Success(notifications);
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while retrieving pending publication notifications.");
            return PublicationNotificationQueryResult.Failure(
                ProviderPublicationRepositoryFailureKind.DatabaseFailure,
                "Database failure while retrieving pending publication notifications.");
        }
    }

    private async Task<PublicationNotificationCompletionResult> ExecuteStateUpdateAsync(
        Guid notificationId,
        string lockOwner,
        string sql,
        PublicationNotificationCompletionOutcome successOutcome,
        Action<SqlCommand> configureParameters,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@NotificationId", SqlDbType.UniqueIdentifier).Value = notificationId;
            command.Parameters.Add("@LockOwner", SqlDbType.NVarChar, 128).Value = lockOwner;
            configureParameters(command);
            int rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            return rowsAffected == 0
                ? PublicationNotificationCompletionResult.Success(PublicationNotificationCompletionOutcome.OwnershipLost)
                : PublicationNotificationCompletionResult.Success(successOutcome);
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while updating publication notification {NotificationId}.", notificationId);
            return PublicationNotificationCompletionResult.Failure(
                ProviderPublicationRepositoryFailureKind.DatabaseFailure,
                "Database failure while updating publication notification.");
        }
    }

    private async Task<PublicationNotificationCompletionResult> ExecuteCompletionDecisionAsync(
        Guid notificationId,
        string lockOwner,
        string sql,
        Action<SqlCommand> configureParameters,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@NotificationId", SqlDbType.UniqueIdentifier).Value = notificationId;
            command.Parameters.Add("@LockOwner", SqlDbType.NVarChar, 128).Value = lockOwner;
            command.Parameters.Add("@SendingState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.Sending;
            configureParameters(command);

            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                string outcomeName = reader.GetString(0);
                string? resolutionReason = reader.IsDBNull(1) ? null : reader.GetString(1);
                return PublicationNotificationCompletionResult.Success(ParseOutcome(outcomeName), resolutionReason);
            }

            return PublicationNotificationCompletionResult.Success(PublicationNotificationCompletionOutcome.OwnershipLost);
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while completing publication notification {NotificationId}.", notificationId);
            return PublicationNotificationCompletionResult.Failure(
                ProviderPublicationRepositoryFailureKind.DatabaseFailure,
                "Database failure while completing publication notification.");
        }
    }

    private static void AddRetryDecisionParameters(SqlCommand command, int? statusCode, string error)
    {
        command.Parameters.Add("@StatusCode", SqlDbType.Int).Value = (object?)statusCode ?? DBNull.Value;
        command.Parameters.Add("@LastError", SqlDbType.NVarChar, ErrorMaxLength).Value = TruncateError(error);
        command.Parameters.Add("@PublicationServiceType", SqlDbType.NVarChar, 32).Value = NotificationServiceTypes.Publication;
        command.Parameters.Add("@RequestServiceType", SqlDbType.NVarChar, 32).Value = NotificationServiceTypes.Request;
        command.Parameters.Add("@ProviderRequestSessionType", SqlDbType.NVarChar, 64).Value = SubscriptionSessionTypes.ProviderRequestSubscription;
        command.Parameters.Add("@OpenSessionState", SqlDbType.NVarChar, 32).Value = SubscriptionSessionStates.Open;
        command.Parameters.Add("@RecipientPendingState", SqlDbType.NVarChar, 32).Value = ProviderPublicationRecipientStates.Pending;
        command.Parameters.Add("@SuppressedState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.Suppressed;
        command.Parameters.Add("@ExpiredState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.Expired;
        command.Parameters.Add("@SessionClosedReason", SqlDbType.NVarChar, 64).Value = PublicationNotificationSuppressionReasons.SessionClosed;
        command.Parameters.Add("@ListenerMissingReason", SqlDbType.NVarChar, 64).Value = PublicationNotificationSuppressionReasons.ListenerMissing;
        command.Parameters.Add("@RecipientNotPendingReason", SqlDbType.NVarChar, 64).Value = PublicationNotificationSuppressionReasons.RecipientNotPending;
        command.Parameters.Add("@PublicationExpiredReason", SqlDbType.NVarChar, 64).Value = PublicationNotificationSuppressionReasons.PublicationExpired;
        command.Parameters.Add("@RequestMissingReason", SqlDbType.NVarChar, 64).Value = PublicationNotificationSuppressionReasons.RequestMissing;
        command.Parameters.Add("@RequestExpiredReason", SqlDbType.NVarChar, 64).Value = PublicationNotificationSuppressionReasons.RequestExpired;
        command.Parameters.Add("@InvalidNotificationShapeReason", SqlDbType.NVarChar, 64).Value =
            PublicationNotificationSuppressionReasons.InvalidNotificationShape;
        command.Parameters.Add("@DeliveryWindowExpiredReason", SqlDbType.NVarChar, 64).Value =
            PublicationNotificationSuppressionReasons.DeliveryWindowExpired;
    }

    private static async Task<IReadOnlyList<string>> ReadTopicsAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select TopicValue
            from dbo.PublicationNotificationTopics
            where PublicationNotificationId = @PublicationNotificationId
            order by TopicOrdinal;
            """;

        List<string> topics = [];
        await using SqlCommand command = new(sql, connection, transaction);
        command.Parameters.Add("@PublicationNotificationId", SqlDbType.UniqueIdentifier).Value = notificationId;

        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            topics.Add(reader.GetString(0));
        }

        return topics;
    }

    private static string? GetSuppressionReason(ClaimedRow row)
    {
        return row.ServiceType switch
        {
            NotificationServiceTypes.Publication => GetPublicationSuppressionReason(row),
            NotificationServiceTypes.Request => GetRequestSuppressionReason(row),
            _ => PublicationNotificationSuppressionReasons.InvalidNotificationShape
        };
    }

    private static string? GetPublicationSuppressionReason(ClaimedRow row)
    {
        if (row.ConsumerSessionId is null || row.ProviderRequestSessionId is not null)
        {
            return PublicationNotificationSuppressionReasons.InvalidNotificationShape;
        }

        if (!string.Equals(row.SessionState, SubscriptionSessionStates.Open, StringComparison.Ordinal))
        {
            return PublicationNotificationSuppressionReasons.SessionClosed;
        }

        if (string.IsNullOrWhiteSpace(row.CurrentListenerUrl))
        {
            return PublicationNotificationSuppressionReasons.ListenerMissing;
        }

        if (!string.Equals(row.RecipientState, ProviderPublicationRecipientStates.Pending, StringComparison.Ordinal))
        {
            return PublicationNotificationSuppressionReasons.RecipientNotPending;
        }

        if (row.ExpiredUtc is not null ||
            row.EffectiveExpiryUtc is DateTime effectiveExpiryUtc && effectiveExpiryUtc <= row.SqlUtcNow)
        {
            return PublicationNotificationSuppressionReasons.PublicationExpired;
        }

        return null;
    }

    private static string? GetRequestSuppressionReason(ClaimedRow row)
    {
        if (row.ProviderRequestSessionId is null || row.ConsumerSessionId is not null)
        {
            return PublicationNotificationSuppressionReasons.InvalidNotificationShape;
        }

        if (!string.Equals(row.SessionType, SubscriptionSessionTypes.ProviderRequestSubscription, StringComparison.Ordinal) ||
            !string.Equals(row.SessionState, SubscriptionSessionStates.Open, StringComparison.Ordinal))
        {
            return PublicationNotificationSuppressionReasons.SessionClosed;
        }

        if (string.IsNullOrWhiteSpace(row.ListenerUrl))
        {
            return PublicationNotificationSuppressionReasons.ListenerMissing;
        }

        if (row.RequestMessageId is null)
        {
            return PublicationNotificationSuppressionReasons.RequestMissing;
        }

        if (row.RequestExpiredUtc is not null ||
            row.RequestEffectiveExpiryUtc is DateTime requestEffectiveExpiryUtc && requestEffectiveExpiryUtc <= row.SqlUtcNow)
        {
            return PublicationNotificationSuppressionReasons.RequestExpired;
        }

        return null;
    }

    private static bool IsDeliveryWindowExpired(ClaimedRow row)
    {
        return row.DeliveryDeadlineUtc is DateTime deadlineUtc && deadlineUtc <= row.SqlUtcNow;
    }

    private static PublicationNotificationCompletionOutcome ParseOutcome(string outcome)
    {
        return outcome switch
        {
            "Delivered" => PublicationNotificationCompletionOutcome.Delivered,
            "RetryScheduled" => PublicationNotificationCompletionOutcome.RetryScheduled,
            "Failed" => PublicationNotificationCompletionOutcome.Failed,
            "Suppressed" => PublicationNotificationCompletionOutcome.Suppressed,
            "Expired" => PublicationNotificationCompletionOutcome.Expired,
            _ => PublicationNotificationCompletionOutcome.OwnershipLost
        };
    }

    private static int GetDelaySeconds(TimeSpan retryDelay)
    {
        if (retryDelay <= TimeSpan.Zero)
        {
            return 0;
        }

        return (int)Math.Min(int.MaxValue, Math.Ceiling(retryDelay.TotalSeconds));
    }

    private static string TruncateError(string error)
    {
        return error.Length <= ErrorMaxLength ? error : error[..ErrorMaxLength];
    }

    private static async Task RollbackQuietlyAsync(SqlTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch
        {
            // Preserve the original exception; rollback failures are not safe to expose to clients.
        }
    }

    private sealed record ClaimedRow(
        Guid NotificationId,
        string ServiceType,
        Guid MessageId,
        Guid? ConsumerSessionId,
        Guid? ProviderRequestSessionId,
        string ListenerUrl,
        int AttemptCount,
        DateTime? NextAttemptUtc,
        DateTime CreatedUtc,
        DateTime? DeliveryDeadlineUtc,
        string? SessionType,
        string? SessionState,
        string? CurrentListenerUrl,
        string? RecipientState,
        DateTime? ExpiredUtc,
        DateTime? EffectiveExpiryUtc,
        Guid? RequestMessageId,
        DateTime? RequestExpiredUtc,
        DateTime? RequestEffectiveExpiryUtc,
        DateTime SqlUtcNow);
}
