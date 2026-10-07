using Microsoft.Data.SqlClient;
using System.Data;

namespace RapidRedPanda.ISBM.Host.Data;

public sealed class SqlSubscriptionSessionRepository : ISubscriptionSessionRepository
{
    private const string ConnectionStringName = "ISBMHost";

    private readonly string _connectionString;
    private readonly ILogger<SqlSubscriptionSessionRepository> _logger;

    public SqlSubscriptionSessionRepository(IConfiguration configuration, ILogger<SqlSubscriptionSessionRepository> logger)
    {
        _connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        _logger = logger;
    }

    public async Task<InsertSubscriptionSessionResult> InsertConsumerSubscriptionSessionAsync(
        ConsumerSubscriptionSessionCreateData session,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                const string insertSessionSql = """
                    insert dbo.ConsumerSubscriptionSessions
                    (
                        SessionId,
                        ChannelId,
                        SessionType,
                        SessionState,
                        AzureTopicName,
                        AzureSubscriptionName,
                        AuthenticatedTokenUsername,
                        ListenerUrl,
                        FilterExpressionsJson
                    )
                    values
                    (
                        @SessionId,
                        @ChannelId,
                        @SessionType,
                        @SessionState,
                        @AzureTopicName,
                        @AzureSubscriptionName,
                        @AuthenticatedTokenUsername,
                        @ListenerUrl,
                        @FilterExpressionsJson
                    );
                    """;

                await using (SqlCommand command = new(insertSessionSql, connection, transaction))
                {
                    command.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = session.SessionId;
                    command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = session.ChannelId;
                    command.Parameters.Add("@SessionType", SqlDbType.NVarChar, 64).Value = SubscriptionSessionTypes.ConsumerPublicationSubscription;
                    command.Parameters.Add("@SessionState", SqlDbType.NVarChar, 32).Value = SubscriptionSessionStates.Open;
                    command.Parameters.Add("@AzureTopicName", SqlDbType.NVarChar, 260).Value = session.AzureTopicName;
                    command.Parameters.Add("@AzureSubscriptionName", SqlDbType.NVarChar, 260).Value = session.AzureSubscriptionName;
                    command.Parameters.Add("@AuthenticatedTokenUsername", SqlDbType.NVarChar, 256).Value =
                        (object?)session.AuthenticatedTokenUsername ?? DBNull.Value;
                    command.Parameters.Add("@ListenerUrl", SqlDbType.NVarChar, 2048).Value =
                        (object?)session.ListenerUrl ?? DBNull.Value;
                    command.Parameters.Add("@FilterExpressionsJson", SqlDbType.NVarChar, -1).Value =
                        (object?)session.FilterExpressionsJson ?? DBNull.Value;

                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                for (int index = 0; index < session.RequestedTopics.Count; index++)
                {
                    const string insertTopicSql = """
                        insert dbo.ConsumerSubscriptionSessionTopics
                        (
                            SessionId,
                            TopicOrdinal,
                            TopicValue
                        )
                        values
                        (
                            @SessionId,
                            @TopicOrdinal,
                            @TopicValue
                        );
                        """;

                    await using SqlCommand command = new(insertTopicSql, connection, transaction);
                    command.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = session.SessionId;
                    command.Parameters.Add("@TopicOrdinal", SqlDbType.Int).Value = index;
                    command.Parameters.Add("@TopicValue", SqlDbType.NVarChar, 512).Value = session.RequestedTopics[index];

                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await RollbackQuietlyAsync(transaction, cancellationToken);
                throw;
            }

            return InsertSubscriptionSessionResult.Success();
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while inserting subscription session {SessionId}.", session.SessionId);
            return InsertSubscriptionSessionResult.Failure(InsertSubscriptionSessionFailureKind.DatabaseFailure, "Database failure while inserting subscription session.");
        }
    }

    public async Task<GetSubscriptionSessionResult> GetSubscriptionSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                select
                    SessionId,
                    ChannelId,
                    SessionType,
                    SessionState,
                    AzureTopicName,
                    AzureSubscriptionName,
                    AuthenticatedTokenUsername,
                    OpenedUtc,
                    ClosedUtc,
                    ListenerUrl,
                    FilterExpressionsJson
                from dbo.ConsumerSubscriptionSessions
                where SessionId = @SessionId;
                """;

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = sessionId;

            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return GetSubscriptionSessionResult.Failure(GetSubscriptionSessionFailureKind.NotFound, "Subscription session was not found.");
            }

            ConsumerSubscriptionSessionData session = new(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                [],
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetDateTime(7),
                reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10));

            await reader.CloseAsync();

            const string topicsSql = """
                select TopicValue
                from dbo.ConsumerSubscriptionSessionTopics
                where SessionId = @SessionId
                order by TopicOrdinal;
                """;

            List<string> requestedTopics = [];
            await using (SqlCommand topicsCommand = new(topicsSql, connection))
            {
                topicsCommand.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = sessionId;
                await using SqlDataReader topicsReader = await topicsCommand.ExecuteReaderAsync(cancellationToken);
                while (await topicsReader.ReadAsync(cancellationToken))
                {
                    requestedTopics.Add(topicsReader.GetString(0));
                }
            }

            return GetSubscriptionSessionResult.Success(session with { RequestedTopics = requestedTopics });
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while retrieving subscription session {SessionId}.", sessionId);
            return GetSubscriptionSessionResult.Failure(GetSubscriptionSessionFailureKind.DatabaseFailure, "Database failure while retrieving subscription session.");
        }
    }

    public async Task<IReadOnlyList<ConsumerSubscriptionSessionData>> GetConsumerSubscriptionSessionsForChannelAsync(
        Guid channelId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                select
                    SessionId,
                    ChannelId,
                    SessionType,
                    SessionState,
                    AzureTopicName,
                    AzureSubscriptionName,
                    AuthenticatedTokenUsername,
                    OpenedUtc,
                    ClosedUtc,
                    ListenerUrl,
                    FilterExpressionsJson
                from dbo.ConsumerSubscriptionSessions
                where ChannelId = @ChannelId;
                """;

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = channelId;

            List<ConsumerSubscriptionSessionData> sessions = [];
            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                sessions.Add(ReadSubscriptionSession(reader));
            }

            return sessions;
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while retrieving subscription sessions for channel {ChannelId}.", channelId);
            throw;
        }
    }

    public async Task<CloseSubscriptionSessionResult> CloseSubscriptionSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                const string sql = """
                    update dbo.ConsumerSubscriptionSessions
                       set SessionState = @ClosedState,
                           ClosedUtc = coalesce(ClosedUtc, @ClosedUtc)
                     where SessionId = @SessionId;
                    """;

                DateTime closedUtc = DateTime.UtcNow;
                await using (SqlCommand command = new(sql, connection, transaction))
                {
                    command.Parameters.Add("@ClosedState", SqlDbType.NVarChar, 32).Value = SubscriptionSessionStates.Closed;
                    command.Parameters.Add("@ClosedUtc", SqlDbType.DateTime2).Value = closedUtc;
                    command.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = sessionId;

                    int affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
                    if (affectedRows == 0)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return CloseSubscriptionSessionResult.Failure(CloseSubscriptionSessionFailureKind.NotFound, "Subscription session was not found.");
                    }
                }

                await transaction.CommitAsync(cancellationToken);
                return CloseSubscriptionSessionResult.Success();
            }
            catch
            {
                await RollbackQuietlyAsync(transaction, cancellationToken);
                throw;
            }
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while closing subscription session {SessionId}.", sessionId);
            return CloseSubscriptionSessionResult.Failure(CloseSubscriptionSessionFailureKind.DatabaseFailure, "Database failure while closing subscription session.");
        }
    }

    private static ConsumerSubscriptionSessionData ReadSubscriptionSession(SqlDataReader reader)
    {
        return new ConsumerSubscriptionSessionData(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            [],
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetDateTime(7),
            reader.IsDBNull(8) ? null : reader.GetDateTime(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10));
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
