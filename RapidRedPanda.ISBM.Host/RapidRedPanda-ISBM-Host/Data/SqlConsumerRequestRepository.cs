using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using RapidRedPanda.ISBM.Host.Services;
using System.Data;

namespace RapidRedPanda.ISBM.Host.Data;

public sealed class SqlConsumerRequestRepository : IConsumerRequestRepository
{
    private const string ConnectionStringName = "ISBMHost";

    private readonly string _connectionString;
    private readonly PublicationNotificationOptions _notificationOptions;
    private readonly ILogger<SqlConsumerRequestRepository> _logger;

    public SqlConsumerRequestRepository(
        IConfiguration configuration,
        IOptions<PublicationNotificationOptions> notificationOptions,
        ILogger<SqlConsumerRequestRepository> logger)
    {
        _connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
        _notificationOptions = notificationOptions.Value;
        _logger = logger;
    }

    public async Task<ConsumerRequestRepositoryResult> InsertPostedRequestAsync(
        ConsumerRequestCreateData request,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                const string insertRequestSql = """
                    insert dbo.ConsumerRequests
                    (
                        MessageId,
                        ConsumerSessionId,
                        ChannelId,
                        CreatedUtc,
                        EffectiveExpiryUtc
                    )
                    values
                    (
                        @MessageId,
                        @ConsumerSessionId,
                        @ChannelId,
                        @CreatedUtc,
                        @EffectiveExpiryUtc
                    );
                    """;

                await using (SqlCommand command = new(insertRequestSql, connection, transaction))
                {
                    command.Parameters.Add("@MessageId", SqlDbType.UniqueIdentifier).Value = request.MessageId;
                    command.Parameters.Add("@ConsumerSessionId", SqlDbType.UniqueIdentifier).Value = request.ConsumerSessionId;
                    command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = request.ChannelId;
                    command.Parameters.Add("@CreatedUtc", SqlDbType.DateTime2).Value = request.CreatedUtc;
                    command.Parameters.Add("@EffectiveExpiryUtc", SqlDbType.DateTime2).Value =
                        (object?)request.EffectiveExpiryUtc ?? DBNull.Value;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                for (int index = 0; index < request.Topics.Count; index++)
                {
                    const string insertTopicSql = """
                        insert dbo.ConsumerRequestTopics
                        (
                            MessageId,
                            TopicOrdinal,
                            TopicValue
                        )
                        values
                        (
                            @MessageId,
                            @TopicOrdinal,
                            @TopicValue
                        );
                        """;

                    await using SqlCommand command = new(insertTopicSql, connection, transaction);
                    command.Parameters.Add("@MessageId", SqlDbType.UniqueIdentifier).Value = request.MessageId;
                    command.Parameters.Add("@TopicOrdinal", SqlDbType.Int).Value = index;
                    command.Parameters.Add("@TopicValue", SqlDbType.NVarChar, 512).Value = request.Topics[index];
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await InsertRequestNotificationsAsync(
                    connection,
                    transaction,
                    request,
                    _notificationOptions.MaximumDeliveryAgeMinutes,
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                return ConsumerRequestRepositoryResult.Success();
            }
            catch
            {
                await RollbackQuietlyAsync(transaction, cancellationToken);
                throw;
            }
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while inserting consumer request metadata. MessageId: {MessageId}.", request.MessageId);
            return ConsumerRequestRepositoryResult.Failure(
                ConsumerRequestRepositoryFailureKind.DatabaseFailure,
                "Database failure while inserting consumer request metadata.");
        }
    }

    private static async Task InsertRequestNotificationsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ConsumerRequestCreateData request,
        int maximumDeliveryAgeMinutes,
        CancellationToken cancellationToken)
    {
        const string notificationSql = """
            declare @SqlUtcNow datetime2(7) = sysutcdatetime();
            declare @ConfiguredDeadlineUtc datetime2(7) = dateadd(minute, @MaximumDeliveryAgeMinutes, @SqlUtcNow);
            declare @DeliveryDeadlineUtc datetime2(7) =
                case
                    when @EffectiveExpiryUtc is not null and @EffectiveExpiryUtc < @ConfiguredDeadlineUtc then @EffectiveExpiryUtc
                    else @ConfiguredDeadlineUtc
                end;

            declare @CreatedNotifications table
            (
                PublicationNotificationId uniqueidentifier not null,
                ProviderRequestSessionId uniqueidentifier not null
            );

            insert dbo.PublicationNotifications
            (
                PublicationNotificationId,
                NotificationServiceType,
                MessageId,
                ConsumerSessionId,
                ProviderRequestSessionId,
                ListenerUrl,
                State,
                NextAttemptUtc,
                DeliveryDeadlineUtc
            )
            output inserted.PublicationNotificationId, inserted.ProviderRequestSessionId
            into @CreatedNotifications (PublicationNotificationId, ProviderRequestSessionId)
            select
                newid(),
                @RequestServiceType,
                @MessageId,
                null,
                session.SessionId,
                session.ListenerUrl,
                @PendingState,
                @SqlUtcNow,
                @DeliveryDeadlineUtc
            from dbo.ConsumerSubscriptionSessions session
            where session.ChannelId = @ChannelId
              and session.SessionType = @ProviderRequestSessionType
              and session.SessionState = @OpenSessionState
              and session.OpenedUtc <= @CreatedUtc
              and session.ListenerUrl is not null
              and ltrim(rtrim(session.ListenerUrl)) <> N''
              and (@EffectiveExpiryUtc is null or @EffectiveExpiryUtc > @SqlUtcNow)
              and exists
              (
                  select 1
                  from dbo.ConsumerSubscriptionSessionTopics sessionTopic
                  inner join dbo.ConsumerRequestTopics requestTopic
                          on requestTopic.TopicValue = sessionTopic.TopicValue
                  where sessionTopic.SessionId = session.SessionId
                    and requestTopic.MessageId = @MessageId
              )
              and not exists
              (
                  select 1
                  from dbo.PublicationNotifications existing with (updlock, holdlock)
                  where existing.MessageId = @MessageId
                    and existing.ProviderRequestSessionId = session.SessionId
                    and existing.NotificationServiceType = @RequestServiceType
              );

            insert dbo.PublicationNotificationTopics
            (
                PublicationNotificationId,
                TopicOrdinal,
                TopicValue
            )
            select
                created.PublicationNotificationId,
                topic.TopicOrdinal,
                topic.TopicValue
            from @CreatedNotifications created
            cross join dbo.ConsumerRequestTopics topic
            where topic.MessageId = @MessageId
              and not exists
              (
                  select 1
                  from dbo.PublicationNotificationTopics existingTopic
                  where existingTopic.PublicationNotificationId = created.PublicationNotificationId
                    and existingTopic.TopicValue = topic.TopicValue
              );
            """;

        await using SqlCommand command = new(notificationSql, connection, transaction);
        command.Parameters.Add("@MessageId", SqlDbType.UniqueIdentifier).Value = request.MessageId;
        command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = request.ChannelId;
        command.Parameters.Add("@CreatedUtc", SqlDbType.DateTime2).Value = request.CreatedUtc;
        command.Parameters.Add("@EffectiveExpiryUtc", SqlDbType.DateTime2).Value = (object?)request.EffectiveExpiryUtc ?? DBNull.Value;
        command.Parameters.Add("@MaximumDeliveryAgeMinutes", SqlDbType.Int).Value = maximumDeliveryAgeMinutes;
        command.Parameters.Add("@RequestServiceType", SqlDbType.NVarChar, 32).Value = NotificationServiceTypes.Request;
        command.Parameters.Add("@PendingState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.Pending;
        command.Parameters.Add("@ProviderRequestSessionType", SqlDbType.NVarChar, 64).Value = SubscriptionSessionTypes.ProviderRequestSubscription;
        command.Parameters.Add("@OpenSessionState", SqlDbType.NVarChar, 32).Value = SubscriptionSessionStates.Open;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ConsumerRequestRepositoryResult> ExpireRequestAsync(
        Guid consumerSessionId,
        Guid channelId,
        Guid messageId,
        DateTime expiredUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                const string sql = """
                    update dbo.ConsumerRequests
                       set ExpiredUtc = coalesce(ExpiredUtc, @ExpiredUtc)
                     where MessageId = @MessageId
                       and ConsumerSessionId = @ConsumerSessionId
                       and ChannelId = @ChannelId;
                    """;

                await using (SqlCommand command = new(sql, connection, transaction))
                {
                    command.Parameters.Add("@ExpiredUtc", SqlDbType.DateTime2).Value = expiredUtc;
                    command.Parameters.Add("@MessageId", SqlDbType.UniqueIdentifier).Value = messageId;
                    command.Parameters.Add("@ConsumerSessionId", SqlDbType.UniqueIdentifier).Value = consumerSessionId;
                    command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = channelId;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                int suppressedCount = await SqlRequestNotificationSuppression.SuppressForMessageAsync(
                    connection,
                    transaction,
                    messageId,
                    expiredUtc,
                    PublicationNotificationSuppressionReasons.RequestExpired,
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                _logger.LogInformation(
                    "Suppressed {SuppressedCount} request notifications while expiring request {MessageId}.",
                    suppressedCount,
                    messageId);
                return ConsumerRequestRepositoryResult.Success();
            }
            catch
            {
                await RollbackQuietlyAsync(transaction, cancellationToken);
                throw;
            }
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while expiring consumer request {MessageId}.", messageId);
            return ConsumerRequestRepositoryResult.Failure(
                ConsumerRequestRepositoryFailureKind.DatabaseFailure,
                "Database failure while expiring consumer request.");
        }
    }

    public async Task<RequestExpirationStateResult> IsRequestExpiredAsync(
        Guid messageId,
        Guid providerSessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                select case when exists
                (
                    select 1
                    from dbo.ConsumerRequests request
                    inner join dbo.ConsumerSubscriptionSessions session
                            on session.ChannelId = request.ChannelId
                    where request.MessageId = @MessageId
                      and request.ExpiredUtc is not null
                      and session.SessionId = @ProviderSessionId
                      and session.SessionType = @ProviderRequestSessionType
                )
                then cast(1 as bit) else cast(0 as bit) end;
                """;

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@MessageId", SqlDbType.UniqueIdentifier).Value = messageId;
            command.Parameters.Add("@ProviderSessionId", SqlDbType.UniqueIdentifier).Value = providerSessionId;
            command.Parameters.Add("@ProviderRequestSessionType", SqlDbType.NVarChar, 64).Value = SubscriptionSessionTypes.ProviderRequestSubscription;

            bool isExpired = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
            return RequestExpirationStateResult.Success(isExpired);
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while checking request expiration. MessageId: {MessageId}. ProviderSessionId: {ProviderSessionId}.", messageId, providerSessionId);
            return RequestExpirationStateResult.Failure(
                ConsumerRequestRepositoryFailureKind.DatabaseFailure,
                "Database failure while checking request expiration.");
        }
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
}
