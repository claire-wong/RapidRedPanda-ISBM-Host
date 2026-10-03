using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using RapidRedPanda.ISBM.Host.Services;
using System.Data;
using System.Globalization;
using System.Text;

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
                        EffectiveExpiryUtc,
                        MessageContent,
                        MediaType,
                        Payload,
                        PayloadKind,
                        ContentEncoding
                    )
                    values
                    (
                        @MessageId,
                        @ConsumerSessionId,
                        @ChannelId,
                        @CreatedUtc,
                        @EffectiveExpiryUtc,
                        @MessageContent,
                        @MediaType,
                        @Payload,
                        @PayloadKind,
                        @ContentEncoding
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
                    AddMessageContentParameters(command, request.MessageContent);
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
                    inner join dbo.ProviderRequestSessions session
                            on session.ChannelId = request.ChannelId
                    where request.MessageId = @MessageId
                      and session.SessionId = @ProviderSessionId
                      and
                      (
                          request.ExpiredUtc is not null
                          or request.EffectiveExpiryUtc <= sysutcdatetime()
                      )
                )
                then cast(1 as bit) else cast(0 as bit) end;
                """;

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@MessageId", SqlDbType.UniqueIdentifier).Value = messageId;
            command.Parameters.Add("@ProviderSessionId", SqlDbType.UniqueIdentifier).Value = providerSessionId;
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

    public async Task<ReadConsumerRequestResult> GetOutstandingRequestAsync(
        Guid providerSessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                select top (1)
                       request.MessageId,
                       request.ConsumerSessionId,
                       request.ChannelId,
                       recipient.TopicValue,
                       request.MessageContent,
                       request.MediaType,
                       request.Payload,
                       request.PayloadKind,
                       request.ContentEncoding
                from dbo.ConsumerRequestRecipients recipient
                inner join dbo.ConsumerRequests request
                        on request.MessageId = recipient.RequestMessageId
                where recipient.ProviderRequestSessionId = @ProviderRequestSessionId
                  and recipient.State = @OutstandingState
                order by recipient.ReadUtc, request.CreatedUtc;
                """;

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@ProviderRequestSessionId", SqlDbType.UniqueIdentifier).Value = providerSessionId;
            command.Parameters.Add("@OutstandingState", SqlDbType.NVarChar, 32).Value = ConsumerRequestRecipientStates.Outstanding;
            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            ConsumerRequestData? request = await ReadRequestDataAsync(reader, cancellationToken);
            return request is null
                ? ReadConsumerRequestResult.NoRequest()
                : ReadConsumerRequestResult.Success(request);
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while reading outstanding request for provider session {ProviderSessionId}.", providerSessionId);
            return ReadConsumerRequestResult.Failure(
                ConsumerRequestRepositoryFailureKind.DatabaseFailure,
                "Database failure while reading outstanding request.");
        }
    }

    public async Task<ReadConsumerRequestResult> TryMarkRequestOutstandingAsync(
        Guid messageId,
        Guid providerSessionId,
        Guid channelId,
        IReadOnlyList<string> providerTopics,
        CancellationToken cancellationToken)
    {
        if (providerTopics.Count == 0)
        {
            return ReadConsumerRequestResult.NoRequest();
        }

        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            try
            {
                ConsumerRequestData? existingOutstanding = await GetOutstandingRequestAsync(
                    connection,
                    transaction,
                    providerSessionId,
                    cancellationToken);
                if (existingOutstanding is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return ReadConsumerRequestResult.Success(existingOutstanding);
                }

                string topicValuesSql = BuildTopicValuesSql(providerTopics);
                string claimSql = $$"""
                    declare @SqlUtcNow datetime2(7) = sysutcdatetime();

                    declare @Candidate table
                    (
                        MessageId uniqueidentifier not null,
                        ConsumerSessionId uniqueidentifier not null,
                        ChannelId uniqueidentifier not null,
                        TopicValue nvarchar(512) not null,
                        MessageContent nvarchar(max) null,
                        MediaType nvarchar(256) null,
                        Payload varbinary(max) null,
                        PayloadKind nvarchar(16) null,
                        ContentEncoding nvarchar(64) null
                    );

                    insert @Candidate
                    (
                        MessageId,
                        ConsumerSessionId,
                        ChannelId,
                        TopicValue,
                        MessageContent,
                        MediaType,
                        Payload,
                        PayloadKind,
                        ContentEncoding
                    )
                    select top (1)
                           request.MessageId,
                           request.ConsumerSessionId,
                           request.ChannelId,
                           topic.TopicValue,
                           request.MessageContent,
                           request.MediaType,
                           request.Payload,
                           request.PayloadKind,
                           request.ContentEncoding
                    from dbo.ConsumerRequests request with (updlock, holdlock)
                    inner join dbo.ConsumerRequestTopics topic
                            on topic.MessageId = request.MessageId
                    inner join ({{topicValuesSql}}) providerTopic(TopicValue)
                            on providerTopic.TopicValue = topic.TopicValue
                    where request.MessageId = @MessageId
                      and request.ChannelId = @ChannelId
                      and request.ExpiredUtc is null
                      and (request.EffectiveExpiryUtc is null or request.EffectiveExpiryUtc > @SqlUtcNow)
                      and request.MessageContent is not null
                      and not exists
                      (
                          select 1
                          from dbo.ConsumerRequestRecipients removed
                          where removed.RequestMessageId = request.MessageId
                            and removed.ProviderRequestSessionId = @ProviderRequestSessionId
                            and removed.State = @RemovedState
                      )
                    order by topic.TopicOrdinal;

                    insert dbo.ConsumerRequestRecipients
                    (
                        RequestMessageId,
                        ProviderRequestSessionId,
                        State,
                        TopicValue,
                        ReadUtc
                    )
                    select
                        candidate.MessageId,
                        @ProviderRequestSessionId,
                        @OutstandingState,
                        candidate.TopicValue,
                        @SqlUtcNow
                    from @Candidate candidate
                    where not exists
                    (
                        select 1
                        from dbo.ConsumerRequestRecipients existing
                        where existing.RequestMessageId = candidate.MessageId
                          and existing.ProviderRequestSessionId = @ProviderRequestSessionId
                    );

                    select top (1)
                           candidate.MessageId,
                           candidate.ConsumerSessionId,
                           candidate.ChannelId,
                           candidate.TopicValue,
                           candidate.MessageContent,
                           candidate.MediaType,
                           candidate.Payload,
                           candidate.PayloadKind,
                           candidate.ContentEncoding
                    from @Candidate candidate;
                    """;

                await using SqlCommand command = new(claimSql, connection, transaction);
                command.Parameters.Add("@MessageId", SqlDbType.UniqueIdentifier).Value = messageId;
                command.Parameters.Add("@ProviderRequestSessionId", SqlDbType.UniqueIdentifier).Value = providerSessionId;
                command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = channelId;
                command.Parameters.Add("@OutstandingState", SqlDbType.NVarChar, 32).Value = ConsumerRequestRecipientStates.Outstanding;
                command.Parameters.Add("@RemovedState", SqlDbType.NVarChar, 32).Value = ConsumerRequestRecipientStates.Removed;
                AddTopicParameters(command, providerTopics);

                await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                ConsumerRequestData? claimed = await ReadRequestDataAsync(reader, cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                return claimed is null
                    ? ReadConsumerRequestResult.NoRequest()
                    : ReadConsumerRequestResult.Success(claimed);
            }
            catch
            {
                await RollbackQuietlyAsync(transaction, cancellationToken);
                throw;
            }
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while marking request {MessageId} outstanding for provider session {ProviderSessionId}.", messageId, providerSessionId);
            return ReadConsumerRequestResult.Failure(
                ConsumerRequestRepositoryFailureKind.DatabaseFailure,
                "Database failure while marking request outstanding.");
        }
    }

    public async Task<RemoveConsumerRequestRecipientResult> RemoveOutstandingRequestAsync(
        Guid providerSessionId,
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
                    declare @Removed table (RequestMessageId uniqueidentifier not null);

                    update dbo.ConsumerRequestRecipients
                       set State = @RemovedState,
                           ResolvedUtc = coalesce(ResolvedUtc, sysutcdatetime())
                    output inserted.RequestMessageId into @Removed (RequestMessageId)
                     where ProviderRequestSessionId = @ProviderRequestSessionId
                       and State = @OutstandingState;

                    select top (1) RequestMessageId from @Removed;
                    """;

                await using SqlCommand command = new(sql, connection, transaction);
                command.Parameters.Add("@ProviderRequestSessionId", SqlDbType.UniqueIdentifier).Value = providerSessionId;
                command.Parameters.Add("@OutstandingState", SqlDbType.NVarChar, 32).Value = ConsumerRequestRecipientStates.Outstanding;
                command.Parameters.Add("@RemovedState", SqlDbType.NVarChar, 32).Value = ConsumerRequestRecipientStates.Removed;
                object? result = await command.ExecuteScalarAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return RemoveConsumerRequestRecipientResult.Success(result is Guid messageId ? messageId : null);
            }
            catch
            {
                await RollbackQuietlyAsync(transaction, cancellationToken);
                throw;
            }
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while removing outstanding request for provider session {ProviderSessionId}.", providerSessionId);
            return RemoveConsumerRequestRecipientResult.Failure(
                ConsumerRequestRepositoryFailureKind.DatabaseFailure,
                "Database failure while removing outstanding request.");
        }
    }

    public async Task<RequestResponseEligibilityResult> FindRespondableRequestAsync(
        Guid requestMessageId,
        Guid providerSessionId,
        Guid channelId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                select top (1) request.ConsumerSessionId
                from dbo.ConsumerRequests request
                where request.MessageId = @RequestMessageId
                  and request.ChannelId = @ChannelId
                  and
                  (
                      (
                          request.ExpiredUtc is null
                          and (request.EffectiveExpiryUtc is null or request.EffectiveExpiryUtc > sysutcdatetime())
                      )
                      or exists
                      (
                          select 1
                          from dbo.ConsumerRequestRecipients recipient
                          where recipient.RequestMessageId = request.MessageId
                            and recipient.ProviderRequestSessionId = @ProviderSessionId
                            and recipient.State = @OutstandingState
                      )
                  );
                """;

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@RequestMessageId", SqlDbType.UniqueIdentifier).Value = requestMessageId;
            command.Parameters.Add("@ProviderSessionId", SqlDbType.UniqueIdentifier).Value = providerSessionId;
            command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = channelId;
            command.Parameters.Add("@OutstandingState", SqlDbType.NVarChar, 32).Value = ConsumerRequestRecipientStates.Outstanding;

            object? result = await command.ExecuteScalarAsync(cancellationToken);
            return result is Guid consumerSessionId
                ? RequestResponseEligibilityResult.Success(consumerSessionId)
                : RequestResponseEligibilityResult.NoMatch();
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while matching respondable request. RequestMessageId: {RequestMessageId}. ProviderSessionId: {ProviderSessionId}.", requestMessageId, providerSessionId);
            return RequestResponseEligibilityResult.Failure(
                ConsumerRequestRepositoryFailureKind.DatabaseFailure,
                "Database failure while matching respondable request.");
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
                NotificationId uniqueidentifier not null,
                ProviderRequestSessionId uniqueidentifier not null
            );

            insert dbo.Notifications
            (
                NotificationId,
                NotificationServiceType,
                MessageId,
                ConsumerSubscriptionSessionId,
                ProviderRequestSessionId,
                ConsumerRequestSessionId,
                ListenerUrl,
                State,
                NextAttemptUtc,
                DeliveryDeadlineUtc
            )
            output inserted.NotificationId, inserted.ProviderRequestSessionId
            into @CreatedNotifications (NotificationId, ProviderRequestSessionId)
            select
                newid(),
                @RequestServiceType,
                @MessageId,
                null,
                session.SessionId,
                null,
                session.ListenerUrl,
                @PendingState,
                @SqlUtcNow,
                @DeliveryDeadlineUtc
            from dbo.ProviderRequestSessions session
            where session.ChannelId = @ChannelId
              and session.SessionState = @OpenSessionState
              and session.OpenedUtc <= @CreatedUtc
              and session.ListenerUrl is not null
              and ltrim(rtrim(session.ListenerUrl)) <> N''
              and (@EffectiveExpiryUtc is null or @EffectiveExpiryUtc > @SqlUtcNow)
              and exists
              (
                  select 1
                  from dbo.ProviderRequestSessionTopics sessionTopic
                  inner join dbo.ConsumerRequestTopics requestTopic
                          on requestTopic.TopicValue = sessionTopic.TopicValue
                  where sessionTopic.ProviderRequestSessionId = session.SessionId
                    and requestTopic.MessageId = @MessageId
              )
              and not exists
              (
                  select 1
                  from dbo.Notifications existing with (updlock, holdlock)
                  where existing.MessageId = @MessageId
                    and existing.ProviderRequestSessionId = session.SessionId
                    and existing.NotificationServiceType = @RequestServiceType
              );

            insert dbo.NotificationTopics
            (
                NotificationId,
                TopicOrdinal,
                TopicValue
            )
            select
                created.NotificationId,
                topic.TopicOrdinal,
                topic.TopicValue
            from @CreatedNotifications created
            cross join dbo.ConsumerRequestTopics topic
            where topic.MessageId = @MessageId
              and not exists
              (
                  select 1
                  from dbo.NotificationTopics existingTopic
                  where existingTopic.NotificationId = created.NotificationId
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
        command.Parameters.Add("@OpenSessionState", SqlDbType.NVarChar, 32).Value = SubscriptionSessionStates.Open;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ConsumerRequestData?> GetOutstandingRequestAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid providerSessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select top (1)
                   request.MessageId,
                   request.ConsumerSessionId,
                   request.ChannelId,
                   recipient.TopicValue,
                   request.MessageContent,
                   request.MediaType,
                   request.Payload,
                   request.PayloadKind,
                   request.ContentEncoding
            from dbo.ConsumerRequestRecipients recipient with (updlock, holdlock)
            inner join dbo.ConsumerRequests request
                    on request.MessageId = recipient.RequestMessageId
            where recipient.ProviderRequestSessionId = @ProviderRequestSessionId
              and recipient.State = @OutstandingState
            order by recipient.ReadUtc, request.CreatedUtc;
            """;

        await using SqlCommand command = new(sql, connection, transaction);
        command.Parameters.Add("@ProviderRequestSessionId", SqlDbType.UniqueIdentifier).Value = providerSessionId;
        command.Parameters.Add("@OutstandingState", SqlDbType.NVarChar, 32).Value = ConsumerRequestRecipientStates.Outstanding;
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await ReadRequestDataAsync(reader, cancellationToken);
    }

    private static async Task<ConsumerRequestData?> ReadRequestDataAsync(
        SqlDataReader reader,
        CancellationToken cancellationToken)
    {
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(4))
        {
            return null;
        }

        return new ConsumerRequestData(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            new PersistedMessageContent(
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : (byte[])reader.GetValue(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
    }

    private static void AddMessageContentParameters(SqlCommand command, PersistedMessageContent content)
    {
        command.Parameters.Add("@MessageContent", SqlDbType.NVarChar, -1).Value = content.LegacyMessageContent;
        command.Parameters.Add("@MediaType", SqlDbType.NVarChar, 256).Value = (object?)content.MediaType ?? DBNull.Value;
        command.Parameters.Add("@Payload", SqlDbType.VarBinary, -1).Value = (object?)content.Payload ?? DBNull.Value;
        command.Parameters.Add("@PayloadKind", SqlDbType.NVarChar, 16).Value = (object?)content.PayloadKind ?? DBNull.Value;
        command.Parameters.Add("@ContentEncoding", SqlDbType.NVarChar, 64).Value = (object?)content.ContentEncoding ?? DBNull.Value;
    }

    private static string BuildTopicValuesSql(IReadOnlyList<string> topics)
    {
        StringBuilder builder = new();
        for (int index = 0; index < topics.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(" union all ");
            }

            builder.Append("select @Topic");
            builder.Append(index.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static void AddTopicParameters(SqlCommand command, IReadOnlyList<string> topics)
    {
        for (int index = 0; index < topics.Count; index++)
        {
            command.Parameters.Add($"@Topic{index}", SqlDbType.NVarChar, 512).Value = topics[index];
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
