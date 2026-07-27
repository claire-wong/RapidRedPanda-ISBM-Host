using Microsoft.Data.SqlClient;
using System.Data;

namespace RapidRedPanda.ISBM.Host.Data;

public sealed class SqlConsumerRequestRepository : IConsumerRequestRepository
{
    private const string ConnectionStringName = "ISBMHost";

    private readonly string _connectionString;
    private readonly ILogger<SqlConsumerRequestRepository> _logger;

    public SqlConsumerRequestRepository(
        IConfiguration configuration,
        ILogger<SqlConsumerRequestRepository> logger)
    {
        _connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
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

            const string sql = """
                update dbo.ConsumerRequests
                   set ExpiredUtc = coalesce(ExpiredUtc, @ExpiredUtc)
                 where MessageId = @MessageId
                   and ConsumerSessionId = @ConsumerSessionId
                   and ChannelId = @ChannelId;
                """;

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@ExpiredUtc", SqlDbType.DateTime2).Value = expiredUtc;
            command.Parameters.Add("@MessageId", SqlDbType.UniqueIdentifier).Value = messageId;
            command.Parameters.Add("@ConsumerSessionId", SqlDbType.UniqueIdentifier).Value = consumerSessionId;
            command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = channelId;
            await command.ExecuteNonQueryAsync(cancellationToken);

            return ConsumerRequestRepositoryResult.Success();
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
