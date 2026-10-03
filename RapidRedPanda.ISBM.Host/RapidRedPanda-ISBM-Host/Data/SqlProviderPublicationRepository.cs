using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using RapidRedPanda.ISBM.Host.Services;
using System.Data;

namespace RapidRedPanda.ISBM.Host.Data;

public sealed class SqlProviderPublicationRepository : IProviderPublicationRepository
{
    private const string ConnectionStringName = "ISBMHost";

    private readonly string _connectionString;
    private readonly PublicationNotificationOptions _notificationOptions;
    private readonly ILogger<SqlProviderPublicationRepository> _logger;

    public SqlProviderPublicationRepository(
        IConfiguration configuration,
        IOptions<PublicationNotificationOptions> notificationOptions,
        ILogger<SqlProviderPublicationRepository> logger)
    {
        _connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
        _notificationOptions = notificationOptions.Value;
        _logger = logger;
    }

    public async Task<ProviderPublicationRepositoryResult> InsertPublishedPublicationAsync(
        ProviderPublicationCreateData publication,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                const string insertPublicationSql = """
                    insert dbo.ProviderPublications
                    (
                        MessageId,
                        ProviderSessionId,
                        ChannelId,
                        PublishedUtc,
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
                        @ProviderSessionId,
                        @ChannelId,
                        @PublishedUtc,
                        @EffectiveExpiryUtc,
                        @MessageContent,
                        @MediaType,
                        @Payload,
                        @PayloadKind,
                        @ContentEncoding
                    );
                    """;

                await using (SqlCommand command = new(insertPublicationSql, connection, transaction))
                {
                    command.Parameters.Add("@MessageId", SqlDbType.UniqueIdentifier).Value = publication.MessageId;
                    command.Parameters.Add("@ProviderSessionId", SqlDbType.UniqueIdentifier).Value = publication.ProviderSessionId;
                    command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = publication.ChannelId;
                    command.Parameters.Add("@PublishedUtc", SqlDbType.DateTime2).Value = publication.PublishedUtc;
                    command.Parameters.Add("@EffectiveExpiryUtc", SqlDbType.DateTime2).Value =
                        (object?)publication.EffectiveExpiryUtc ?? DBNull.Value;
                    AddMessageContentParameters(command, publication.MessageContent);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                for (int index = 0; index < publication.Topics.Count; index++)
                {
                    const string insertTopicSql = """
                        insert dbo.ProviderPublicationTopics
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
                    command.Parameters.Add("@MessageId", SqlDbType.UniqueIdentifier).Value = publication.MessageId;
                    command.Parameters.Add("@TopicOrdinal", SqlDbType.Int).Value = index;
                    command.Parameters.Add("@TopicValue", SqlDbType.NVarChar, 512).Value = publication.Topics[index];
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await InsertRecipientSnapshotAsync(
                    connection,
                    transaction,
                    publication,
                    _notificationOptions.MaximumDeliveryAgeMinutes,
                    cancellationToken);
                await InsertPublicationNotificationsAsync(
                    connection,
                    transaction,
                    publication,
                    _notificationOptions.MaximumDeliveryAgeMinutes,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await RollbackQuietlyAsync(transaction, cancellationToken);
                throw;
            }

            return ProviderPublicationRepositoryResult.Success();
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while inserting provider publication metadata. MessageId: {MessageId}.", publication.MessageId);
            return ProviderPublicationRepositoryResult.Failure(
                ProviderPublicationRepositoryFailureKind.DatabaseFailure,
                "Database failure while inserting provider publication metadata.");
        }
    }

    public async Task<ProviderPublicationRepositoryResult> ExpirePublicationAsync(
        Guid providerSessionId,
        Guid channelId,
        Guid messageId,
        DateTime expiredUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            try
            {
                const string publicationSql = """
                    select ChannelId
                    from dbo.ProviderPublications with (updlock, holdlock)
                    where MessageId = @MessageId;
                    """;

                Guid? storedChannelId = null;
                await using (SqlCommand command = new(publicationSql, connection, transaction))
                {
                    command.Parameters.Add("@MessageId", SqlDbType.UniqueIdentifier).Value = messageId;
                    object? result = await command.ExecuteScalarAsync(cancellationToken);
                    if (result is Guid foundChannelId)
                    {
                        storedChannelId = foundChannelId;
                    }
                }

                if (storedChannelId is null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return ProviderPublicationRepositoryResult.Failure(
                        ProviderPublicationRepositoryFailureKind.NotFound,
                        "Publication was not found.");
                }

                if (storedChannelId.Value != channelId)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return ProviderPublicationRepositoryResult.Failure(
                        ProviderPublicationRepositoryFailureKind.ChannelMismatch,
                        "Publication belongs to a different channel.");
                }

                const string expireSql = """
                    update dbo.ProviderPublications
                       set ExpiredUtc = coalesce(ExpiredUtc, @ExpiredUtc)
                     where MessageId = @MessageId
                       and ChannelId = @ChannelId;
                    """;

                await using (SqlCommand command = new(expireSql, connection, transaction))
                {
                    command.Parameters.Add("@ExpiredUtc", SqlDbType.DateTime2).Value = expiredUtc;
                    command.Parameters.Add("@MessageId", SqlDbType.UniqueIdentifier).Value = messageId;
                    command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = channelId;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return ProviderPublicationRepositoryResult.Success();
            }
            catch
            {
                await RollbackQuietlyAsync(transaction, cancellationToken);
                throw;
            }
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while expiring provider publication {MessageId}.", messageId);
            return ProviderPublicationRepositoryResult.Failure(
                ProviderPublicationRepositoryFailureKind.DatabaseFailure,
                "Database failure while expiring provider publication.");
        }
    }

    public async Task<PublicationExpirationStateResult> IsPublicationExpiredAsync(
        Guid messageId,
        Guid consumerSessionId,
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
                    from dbo.ProviderPublications publication
                    inner join dbo.ProviderPublicationRecipients recipient
                            on recipient.MessageId = publication.MessageId
                    where publication.MessageId = @MessageId
                      and publication.ExpiredUtc is not null
                      and recipient.ConsumerSessionId = @ConsumerSessionId
                      and recipient.State in (@PendingState, @OutstandingState)
                )
                or exists
                (
                    select 1
                    from dbo.ProviderPublications publication
                    inner join dbo.ProviderPublicationRecipients recipient
                            on recipient.MessageId = publication.MessageId
                    where publication.MessageId = @MessageId
                      and publication.EffectiveExpiryUtc is not null
                      and publication.EffectiveExpiryUtc <= sysutcdatetime()
                      and recipient.ConsumerSessionId = @ConsumerSessionId
                      and recipient.State = @PendingState
                )
                then cast(1 as bit) else cast(0 as bit) end;
                """;

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@MessageId", SqlDbType.UniqueIdentifier).Value = messageId;
            command.Parameters.Add("@ConsumerSessionId", SqlDbType.UniqueIdentifier).Value = consumerSessionId;
            command.Parameters.Add("@PendingState", SqlDbType.NVarChar, 32).Value = ProviderPublicationRecipientStates.Pending;
            command.Parameters.Add("@OutstandingState", SqlDbType.NVarChar, 32).Value = ProviderPublicationRecipientStates.Outstanding;

            bool isExpired = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
            return PublicationExpirationStateResult.Success(isExpired);
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while checking provider publication expiration. MessageId: {MessageId}. ConsumerSessionId: {ConsumerSessionId}.", messageId, consumerSessionId);
            return PublicationExpirationStateResult.Failure(
                ProviderPublicationRepositoryFailureKind.DatabaseFailure,
                "Database failure while checking provider publication expiration.");
        }
    }

    public async Task<ReadProviderPublicationResult> GetOutstandingPublicationAsync(
        Guid consumerSessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);

            ConsumerSessionPublicationData? publication = await GetOutstandingPublicationAsync(
                connection,
                transaction: null,
                consumerSessionId,
                cancellationToken);

            return publication is null
                ? ReadProviderPublicationResult.NoPublication()
                : ReadProviderPublicationResult.Success(publication.ToProviderPublicationData());
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while reading outstanding publication for consumer session {ConsumerSessionId}.", consumerSessionId);
            return ReadProviderPublicationResult.Failure(
                ProviderPublicationRepositoryFailureKind.DatabaseFailure,
                "Database failure while reading outstanding publication.");
        }
    }

    public async Task<ReadProviderPublicationResult> TryMarkPublicationOutstandingAsync(
        Guid messageId,
        Guid consumerSessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            try
            {
                ConsumerSessionPublicationData? existingOutstanding = await GetOutstandingPublicationAsync(
                    connection,
                    transaction,
                    consumerSessionId,
                    cancellationToken);
                if (existingOutstanding is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return ReadProviderPublicationResult.Success(existingOutstanding.ToProviderPublicationData());
                }

                const string claimSql = """
                    declare @SqlUtcNow datetime2(7) = sysutcdatetime();

                    declare @Claimed table (MessageId uniqueidentifier not null);

                    update recipient
                       set State = @OutstandingState,
                           ResolvedUtc = null
                    output inserted.MessageId into @Claimed (MessageId)
                      from dbo.ProviderPublicationRecipients recipient with (updlock, holdlock)
                      inner join dbo.ProviderPublications publication with (updlock, holdlock)
                              on publication.MessageId = recipient.MessageId
                     where recipient.MessageId = @MessageId
                       and recipient.ConsumerSessionId = @ConsumerSessionId
                       and recipient.State = @PendingState
                       and publication.MessageContent is not null
                       and publication.ExpiredUtc is null
                       and (publication.EffectiveExpiryUtc is null or publication.EffectiveExpiryUtc > @SqlUtcNow)
                       and not exists
                       (
                           select 1
                           from dbo.ProviderPublicationRecipients existing with (updlock, holdlock)
                           where existing.ConsumerSessionId = @ConsumerSessionId
                             and existing.State = @OutstandingState
                       );

                    select top (1) MessageId from @Claimed;
                    """;

                await using (SqlCommand command = new(claimSql, connection, transaction))
                {
                    command.Parameters.Add("@MessageId", SqlDbType.UniqueIdentifier).Value = messageId;
                    command.Parameters.Add("@ConsumerSessionId", SqlDbType.UniqueIdentifier).Value = consumerSessionId;
                    command.Parameters.Add("@PendingState", SqlDbType.NVarChar, 32).Value = ProviderPublicationRecipientStates.Pending;
                    command.Parameters.Add("@OutstandingState", SqlDbType.NVarChar, 32).Value = ProviderPublicationRecipientStates.Outstanding;
                    object? result = await command.ExecuteScalarAsync(cancellationToken);
                    if (result is not Guid)
                    {
                        await transaction.CommitAsync(cancellationToken);
                        return ReadProviderPublicationResult.NoPublication();
                    }
                }

                ConsumerSessionPublicationData? claimed = await GetOutstandingPublicationAsync(
                    connection,
                    transaction,
                    consumerSessionId,
                    cancellationToken);

                if (claimed is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return ReadProviderPublicationResult.Failure(
                        ProviderPublicationRepositoryFailureKind.DatabaseFailure,
                        "Publication was marked outstanding but durable message content could not be read.");
                }

                await transaction.CommitAsync(cancellationToken);
                return ReadProviderPublicationResult.Success(claimed.ToProviderPublicationData());
            }
            catch
            {
                await RollbackQuietlyAsync(transaction, cancellationToken);
                throw;
            }
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while marking publication {MessageId} outstanding for consumer session {ConsumerSessionId}.", messageId, consumerSessionId);
            return ReadProviderPublicationResult.Failure(
                ProviderPublicationRepositoryFailureKind.DatabaseFailure,
                "Database failure while marking publication outstanding.");
        }
    }

    public async Task<RemoveProviderPublicationRecipientResult> RemoveOutstandingPublicationAsync(
        Guid consumerSessionId,
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
                    declare @Removed table (MessageId uniqueidentifier not null);

                    ;with Outstanding as
                    (
                        select top (1) *
                        from dbo.ProviderPublicationRecipients with (updlock, holdlock)
                        where ConsumerSessionId = @ConsumerSessionId
                          and State = @OutstandingState
                        order by CreatedUtc, MessageId
                    )
                    update Outstanding
                       set State = @RemovedState,
                           ResolvedUtc = coalesce(ResolvedUtc, sysutcdatetime())
                    output inserted.MessageId into @Removed (MessageId);

                    select top (1) MessageId from @Removed;
                    """;

                await using SqlCommand command = new(sql, connection, transaction);
                command.Parameters.Add("@ConsumerSessionId", SqlDbType.UniqueIdentifier).Value = consumerSessionId;
                command.Parameters.Add("@OutstandingState", SqlDbType.NVarChar, 32).Value = ProviderPublicationRecipientStates.Outstanding;
                command.Parameters.Add("@RemovedState", SqlDbType.NVarChar, 32).Value = ProviderPublicationRecipientStates.Removed;
                object? result = await command.ExecuteScalarAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                return RemoveProviderPublicationRecipientResult.Success(result is Guid messageId ? messageId : null);
            }
            catch
            {
                await RollbackQuietlyAsync(transaction, cancellationToken);
                throw;
            }
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while removing outstanding publication for consumer session {ConsumerSessionId}.", consumerSessionId);
            return RemoveProviderPublicationRecipientResult.Failure(
                ProviderPublicationRepositoryFailureKind.DatabaseFailure,
                "Database failure while removing outstanding publication.");
        }
    }

    public Task<ProviderPublicationRepositoryResult> MarkRecipientOutstandingAsync(
        Guid messageId,
        Guid consumerSessionId,
        CancellationToken cancellationToken)
    {
        return MarkRecipientStateAsync(
            messageId,
            consumerSessionId,
            ProviderPublicationRecipientStates.Outstanding,
            resolvedUtc: null,
            cancellationToken);
    }

    public Task<ProviderPublicationRepositoryResult> MarkRecipientSkippedExpiredAsync(
        Guid messageId,
        Guid consumerSessionId,
        CancellationToken cancellationToken)
    {
        return MarkRecipientStateAsync(
            messageId,
            consumerSessionId,
            ProviderPublicationRecipientStates.SkippedExpired,
            DateTime.UtcNow,
            cancellationToken);
    }

    public Task<ProviderPublicationRepositoryResult> MarkRecipientRemovedAsync(
        Guid messageId,
        Guid consumerSessionId,
        CancellationToken cancellationToken)
    {
        return MarkRecipientStateAsync(
            messageId,
            consumerSessionId,
            ProviderPublicationRecipientStates.Removed,
            DateTime.UtcNow,
            cancellationToken);
    }

    public async Task<ProviderPublicationRepositoryResult> MarkRecipientsSubscriptionDeletedAsync(
        Guid consumerSessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                update dbo.ProviderPublicationRecipients
                   set State = @SubscriptionDeletedState,
                       ResolvedUtc = coalesce(ResolvedUtc, sysutcdatetime())
                 where ConsumerSessionId = @ConsumerSessionId
                   and State in (@PendingState, @OutstandingState);
                """;

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@SubscriptionDeletedState", SqlDbType.NVarChar, 32).Value = ProviderPublicationRecipientStates.SubscriptionDeleted;
            command.Parameters.Add("@PendingState", SqlDbType.NVarChar, 32).Value = ProviderPublicationRecipientStates.Pending;
            command.Parameters.Add("@OutstandingState", SqlDbType.NVarChar, 32).Value = ProviderPublicationRecipientStates.Outstanding;
            command.Parameters.Add("@ConsumerSessionId", SqlDbType.UniqueIdentifier).Value = consumerSessionId;
            await command.ExecuteNonQueryAsync(cancellationToken);

            return ProviderPublicationRepositoryResult.Success();
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while marking subscription-deleted expiration recipients. ConsumerSessionId: {ConsumerSessionId}.", consumerSessionId);
            return ProviderPublicationRepositoryResult.Failure(
                ProviderPublicationRepositoryFailureKind.DatabaseFailure,
                "Database failure while marking subscription-deleted expiration recipients.");
        }
    }

    private static async Task InsertRecipientSnapshotAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ProviderPublicationCreateData publication,
        int maximumDeliveryAgeMinutes,
        CancellationToken cancellationToken)
    {
        const string recipientSql = """
            insert dbo.ProviderPublicationRecipients
            (
                MessageId,
                ConsumerSessionId,
                AzureTopicName,
                AzureSubscriptionName,
                State
            )
            select
                @MessageId,
                session.SessionId,
                session.AzureTopicName,
                session.AzureSubscriptionName,
                @PendingState
            from dbo.ConsumerSubscriptionSessions session
            where session.ChannelId = @ChannelId
              and session.SessionState = @OpenState
              and session.OpenedUtc <= @PublishedUtc
              and exists
              (
                  select 1
                  from dbo.ConsumerSubscriptionSessionTopics consumerTopic
                  inner join dbo.ProviderPublicationTopics publicationTopic
                          on publicationTopic.TopicValue = consumerTopic.TopicValue
                         and publicationTopic.MessageId = @MessageId
                  where consumerTopic.SessionId = session.SessionId
              );
            """;

        await using SqlCommand command = new(recipientSql, connection, transaction);
        command.Parameters.Add("@MessageId", SqlDbType.UniqueIdentifier).Value = publication.MessageId;
        command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = publication.ChannelId;
        command.Parameters.Add("@PublishedUtc", SqlDbType.DateTime2).Value = publication.PublishedUtc;
        command.Parameters.Add("@PendingState", SqlDbType.NVarChar, 32).Value = ProviderPublicationRecipientStates.Pending;
        command.Parameters.Add("@OpenState", SqlDbType.NVarChar, 32).Value = SubscriptionSessionStates.Open;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertPublicationNotificationsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ProviderPublicationCreateData publication,
        int maximumDeliveryAgeMinutes,
        CancellationToken cancellationToken)
    {
        const string notificationSql = """
            declare @CreatedNotifications table
            (
                NotificationId uniqueidentifier not null,
                ConsumerSessionId uniqueidentifier not null
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
            output inserted.NotificationId, inserted.ConsumerSubscriptionSessionId
            into @CreatedNotifications (NotificationId, ConsumerSessionId)
            select
                newid(),
                @PublicationServiceType,
                @MessageId,
                recipient.ConsumerSessionId,
                null,
                null,
                session.ListenerUrl,
                @PendingState,
                @PublishedUtc,
                dateadd(minute, @MaximumDeliveryAgeMinutes, @PublishedUtc)
            from dbo.ProviderPublicationRecipients recipient
            inner join dbo.ConsumerSubscriptionSessions session
                    on session.SessionId = recipient.ConsumerSessionId
            where recipient.MessageId = @MessageId
              and recipient.State = @RecipientPendingState
              and session.ListenerUrl is not null
              and not exists
              (
                  select 1
                  from dbo.Notifications existing
                  where existing.MessageId = recipient.MessageId
                    and existing.ConsumerSubscriptionSessionId = recipient.ConsumerSessionId
                    and existing.NotificationServiceType = @PublicationServiceType
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
            cross join dbo.ProviderPublicationTopics topic
            where topic.MessageId = @MessageId;
            """;

        await using SqlCommand command = new(notificationSql, connection, transaction);
        command.Parameters.Add("@MessageId", SqlDbType.UniqueIdentifier).Value = publication.MessageId;
        command.Parameters.Add("@PublicationServiceType", SqlDbType.NVarChar, 32).Value = NotificationServiceTypes.Publication;
        command.Parameters.Add("@PendingState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.Pending;
        command.Parameters.Add("@RecipientPendingState", SqlDbType.NVarChar, 32).Value = ProviderPublicationRecipientStates.Pending;
        command.Parameters.Add("@PublishedUtc", SqlDbType.DateTime2).Value = publication.PublishedUtc;
        command.Parameters.Add("@MaximumDeliveryAgeMinutes", SqlDbType.Int).Value = maximumDeliveryAgeMinutes;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<ProviderPublicationRepositoryResult> MarkRecipientStateAsync(
        Guid messageId,
        Guid consumerSessionId,
        string state,
        DateTime? resolvedUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                update dbo.ProviderPublicationRecipients
                   set State = @State,
                       ResolvedUtc = @ResolvedUtc
                 where MessageId = @MessageId
                   and ConsumerSessionId = @ConsumerSessionId
                   and State in (@PendingState, @OutstandingState);
                """;

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@State", SqlDbType.NVarChar, 32).Value = state;
            command.Parameters.Add("@ResolvedUtc", SqlDbType.DateTime2).Value = (object?)resolvedUtc ?? DBNull.Value;
            command.Parameters.Add("@MessageId", SqlDbType.UniqueIdentifier).Value = messageId;
            command.Parameters.Add("@ConsumerSessionId", SqlDbType.UniqueIdentifier).Value = consumerSessionId;
            command.Parameters.Add("@PendingState", SqlDbType.NVarChar, 32).Value = ProviderPublicationRecipientStates.Pending;
            command.Parameters.Add("@OutstandingState", SqlDbType.NVarChar, 32).Value = ProviderPublicationRecipientStates.Outstanding;
            await command.ExecuteNonQueryAsync(cancellationToken);

            return ProviderPublicationRepositoryResult.Success();
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while marking publication recipient state. MessageId: {MessageId}. ConsumerSessionId: {ConsumerSessionId}. State: {State}.", messageId, consumerSessionId, state);
            return ProviderPublicationRepositoryResult.Failure(
                ProviderPublicationRepositoryFailureKind.DatabaseFailure,
                "Database failure while marking publication recipient state.");
        }
    }

    private static async Task<ConsumerSessionPublicationData?> GetOutstandingPublicationAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        Guid consumerSessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            ;with Outstanding as
            (
                select top (1)
                       recipient.MessageId,
                       recipient.ConsumerSessionId
                from dbo.ProviderPublicationRecipients recipient with (updlock, holdlock)
                where recipient.ConsumerSessionId = @ConsumerSessionId
                  and recipient.State = @OutstandingState
                order by recipient.CreatedUtc, recipient.MessageId
            )
            select publication.MessageId,
                   publication.ProviderSessionId,
                   publication.ChannelId,
                   topic.TopicOrdinal,
                   topic.TopicValue,
                   publication.MessageContent,
                   publication.MediaType,
                   publication.Payload,
                   publication.PayloadKind,
                   publication.ContentEncoding
            from Outstanding outstanding
            inner join dbo.ProviderPublications publication
                    on publication.MessageId = outstanding.MessageId
            inner join dbo.ProviderPublicationTopics topic
                    on topic.MessageId = publication.MessageId
            where publication.MessageContent is not null
            order by topic.TopicOrdinal;
            """;

        await using SqlCommand command = transaction is null
            ? new SqlCommand(sql, connection)
            : new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@ConsumerSessionId", SqlDbType.UniqueIdentifier).Value = consumerSessionId;
        command.Parameters.Add("@OutstandingState", SqlDbType.NVarChar, 32).Value = ProviderPublicationRecipientStates.Outstanding;

        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await ReadPublicationDataAsync(reader, cancellationToken);
    }

    private static async Task<ConsumerSessionPublicationData?> ReadPublicationDataAsync(
        SqlDataReader reader,
        CancellationToken cancellationToken)
    {
        Guid? messageId = null;
        Guid providerSessionId = Guid.Empty;
        Guid channelId = Guid.Empty;
        string? messageContent = null;
        string? mediaType = null;
        byte[]? payload = null;
        string? payloadKind = null;
        string? contentEncoding = null;
        List<string> topics = [];

        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(5))
            {
                return null;
            }

            messageId ??= reader.GetGuid(0);
            providerSessionId = reader.GetGuid(1);
            channelId = reader.GetGuid(2);
            topics.Add(reader.GetString(4));
            messageContent ??= reader.GetString(5);
            mediaType ??= reader.IsDBNull(6) ? null : reader.GetString(6);
            payload ??= reader.IsDBNull(7) ? null : (byte[])reader.GetValue(7);
            payloadKind ??= reader.IsDBNull(8) ? null : reader.GetString(8);
            contentEncoding ??= reader.IsDBNull(9) ? null : reader.GetString(9);
        }

        if (messageId is not Guid foundMessageId ||
            messageContent is null ||
            topics.Count == 0)
        {
            return null;
        }

        return new ConsumerSessionPublicationData(
            foundMessageId,
            providerSessionId,
            channelId,
            topics,
            new PersistedMessageContent(
                messageContent,
                mediaType,
                payload,
                payloadKind,
                contentEncoding));
    }

    private static void AddMessageContentParameters(SqlCommand command, PersistedMessageContent content)
    {
        command.Parameters.Add("@MessageContent", SqlDbType.NVarChar, -1).Value = content.LegacyMessageContent;
        command.Parameters.Add("@MediaType", SqlDbType.NVarChar, 256).Value = (object?)content.MediaType ?? DBNull.Value;
        command.Parameters.Add("@Payload", SqlDbType.VarBinary, -1).Value = (object?)content.Payload ?? DBNull.Value;
        command.Parameters.Add("@PayloadKind", SqlDbType.NVarChar, 16).Value = (object?)content.PayloadKind ?? DBNull.Value;
        command.Parameters.Add("@ContentEncoding", SqlDbType.NVarChar, 64).Value = (object?)content.ContentEncoding ?? DBNull.Value;
    }

    private sealed record ConsumerSessionPublicationData(
        Guid MessageId,
        Guid ProviderSessionId,
        Guid ChannelId,
        IReadOnlyList<string> Topics,
        PersistedMessageContent MessageContent)
    {
        public ProviderPublicationData ToProviderPublicationData()
        {
            return new ProviderPublicationData(
                MessageId,
                ProviderSessionId,
                ChannelId,
                Topics,
                MessageContent);
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
