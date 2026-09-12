using Microsoft.Data.SqlClient;
using System.Data;

namespace RapidRedPanda.ISBM.Host.Data;

public sealed class SqlProviderRequestSessionRepository : IProviderRequestSessionRepository
{
    private const string ConnectionStringName = "ISBMHost";

    private readonly string _connectionString;
    private readonly ILogger<SqlProviderRequestSessionRepository> _logger;

    public SqlProviderRequestSessionRepository(
        IConfiguration configuration,
        ILogger<SqlProviderRequestSessionRepository> logger)
    {
        _connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
        _logger = logger;
    }

    public async Task<InsertProviderRequestSessionResult> InsertProviderRequestSessionAsync(
        ProviderRequestSessionCreateData session,
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
                    insert dbo.ProviderRequestSessions
                    (
                        SessionId,
                        ChannelId,
                        SessionState,
                        AzureTopicName,
                        AzureSubscriptionName,
                        AuthenticatedTokenUsername,
                        ListenerUrl,
                        FilterExpressionsJson,
                        OpenedUtc
                    )
                    values
                    (
                        @SessionId,
                        @ChannelId,
                        @SessionState,
                        @AzureTopicName,
                        @AzureSubscriptionName,
                        @AuthenticatedTokenUsername,
                        @ListenerUrl,
                        @FilterExpressionsJson,
                        @OpenedUtc
                    );
                    """;

                await using (SqlCommand command = new(insertSessionSql, connection, transaction))
                {
                    command.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = session.SessionId;
                    command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = session.ChannelId;
                    command.Parameters.Add("@SessionState", SqlDbType.NVarChar, 32).Value = SubscriptionSessionStates.Open;
                    command.Parameters.Add("@AzureTopicName", SqlDbType.NVarChar, 260).Value = session.AzureTopicName;
                    command.Parameters.Add("@AzureSubscriptionName", SqlDbType.NVarChar, 260).Value = session.AzureSubscriptionName;
                    command.Parameters.Add("@AuthenticatedTokenUsername", SqlDbType.NVarChar, 256).Value =
                        (object?)session.AuthenticatedTokenUsername ?? DBNull.Value;
                    command.Parameters.Add("@ListenerUrl", SqlDbType.NVarChar, 2048).Value =
                        (object?)session.ListenerUrl ?? DBNull.Value;
                    command.Parameters.Add("@FilterExpressionsJson", SqlDbType.NVarChar, -1).Value =
                        (object?)session.FilterExpressionsJson ?? DBNull.Value;
                    command.Parameters.Add("@OpenedUtc", SqlDbType.DateTime2).Value = DateTime.UtcNow;

                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                for (int index = 0; index < session.RequestedTopics.Count; index++)
                {
                    const string insertTopicSql = """
                        insert dbo.ProviderRequestSessionTopics
                        (
                            ProviderRequestSessionId,
                            TopicOrdinal,
                            TopicValue
                        )
                        values
                        (
                            @ProviderRequestSessionId,
                            @TopicOrdinal,
                            @TopicValue
                        );
                        """;

                    await using SqlCommand command = new(insertTopicSql, connection, transaction);
                    command.Parameters.Add("@ProviderRequestSessionId", SqlDbType.UniqueIdentifier).Value = session.SessionId;
                    command.Parameters.Add("@TopicOrdinal", SqlDbType.Int).Value = index;
                    command.Parameters.Add("@TopicValue", SqlDbType.NVarChar, 512).Value = session.RequestedTopics[index];
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return InsertProviderRequestSessionResult.Success();
            }
            catch
            {
                await RollbackQuietlyAsync(transaction, cancellationToken);
                throw;
            }
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while inserting provider request session {SessionId}.", session.SessionId);
            return InsertProviderRequestSessionResult.Failure(
                InsertProviderRequestSessionFailureKind.DatabaseFailure,
                "Database failure while inserting provider request session.");
        }
    }

    public async Task<GetProviderRequestSessionResult> GetProviderRequestSessionAsync(
        Guid sessionId,
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
                    SessionState,
                    AzureTopicName,
                    AzureSubscriptionName,
                    AuthenticatedTokenUsername,
                    OpenedUtc,
                    ClosedUtc,
                    ListenerUrl,
                    FilterExpressionsJson
                from dbo.ProviderRequestSessions
                where SessionId = @SessionId;
                """;

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = sessionId;

            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return GetProviderRequestSessionResult.Failure(
                    GetProviderRequestSessionFailureKind.NotFound,
                    "Provider request session was not found.");
            }

            ProviderRequestSessionData session = new(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                [],
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetDateTime(6),
                reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9));

            await reader.CloseAsync();

            const string topicsSql = """
                select TopicValue
                from dbo.ProviderRequestSessionTopics
                where ProviderRequestSessionId = @ProviderRequestSessionId
                order by TopicOrdinal;
                """;

            List<string> requestedTopics = [];
            await using (SqlCommand topicsCommand = new(topicsSql, connection))
            {
                topicsCommand.Parameters.Add("@ProviderRequestSessionId", SqlDbType.UniqueIdentifier).Value = sessionId;
                await using SqlDataReader topicsReader = await topicsCommand.ExecuteReaderAsync(cancellationToken);
                while (await topicsReader.ReadAsync(cancellationToken))
                {
                    requestedTopics.Add(topicsReader.GetString(0));
                }
            }

            return GetProviderRequestSessionResult.Success(session with { RequestedTopics = requestedTopics });
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while retrieving provider request session {SessionId}.", sessionId);
            return GetProviderRequestSessionResult.Failure(
                GetProviderRequestSessionFailureKind.DatabaseFailure,
                "Database failure while retrieving provider request session.");
        }
    }

    public async Task<CloseProviderRequestSessionRepositoryResult> CloseProviderRequestSessionAsync(
        Guid sessionId,
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
                    update dbo.ProviderRequestSessions
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
                        return CloseProviderRequestSessionRepositoryResult.Failure(
                            CloseProviderRequestSessionRepositoryFailureKind.NotFound,
                            "Provider request session was not found.");
                    }
                }

                int suppressedCount = await SqlRequestNotificationSuppression.SuppressForSessionAsync(
                    connection,
                    transaction,
                    sessionId,
                    closedUtc,
                    PublicationNotificationSuppressionReasons.ProviderRequestSessionClosed,
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                _logger.LogInformation(
                    "Suppressed {SuppressedCount} request notifications while closing provider request session {SessionId}.",
                    suppressedCount,
                    sessionId);
                return CloseProviderRequestSessionRepositoryResult.Success();
            }
            catch
            {
                await RollbackQuietlyAsync(transaction, cancellationToken);
                throw;
            }
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while closing provider request session {SessionId}.", sessionId);
            return CloseProviderRequestSessionRepositoryResult.Failure(
                CloseProviderRequestSessionRepositoryFailureKind.DatabaseFailure,
                "Database failure while closing provider request session.");
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
