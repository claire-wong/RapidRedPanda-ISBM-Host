using Microsoft.Data.SqlClient;
using System.Data;

namespace RapidRedPanda.ISBM.Host.Data;

public sealed class SqlConsumerRequestSessionRepository : IConsumerRequestSessionRepository
{
    private const string ConnectionStringName = "ISBMHost";

    private readonly string _connectionString;
    private readonly ILogger<SqlConsumerRequestSessionRepository> _logger;

    public SqlConsumerRequestSessionRepository(
        IConfiguration configuration,
        ILogger<SqlConsumerRequestSessionRepository> logger)
    {
        _connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
        _logger = logger;
    }

    public async Task<InsertConsumerRequestSessionResult> InsertConsumerRequestSessionAsync(
        ConsumerRequestSessionCreateData session,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                insert dbo.ConsumerRequestSessions
                (
                    SessionId,
                    ChannelId,
                    SessionState,
                    AuthenticatedTokenUsername
                )
                values
                (
                    @SessionId,
                    @ChannelId,
                    @SessionState,
                    @AuthenticatedTokenUsername
                );
                """;

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = session.SessionId;
            command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = session.ChannelId;
            command.Parameters.Add("@SessionState", SqlDbType.NVarChar, 32).Value = ConsumerRequestSessionStates.Open;
            command.Parameters.Add("@AuthenticatedTokenUsername", SqlDbType.NVarChar, 256).Value =
                (object?)session.AuthenticatedTokenUsername ?? DBNull.Value;

            await command.ExecuteNonQueryAsync(cancellationToken);
            return InsertConsumerRequestSessionResult.Success();
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while inserting consumer request session {SessionId}.", session.SessionId);
            return InsertConsumerRequestSessionResult.Failure(
                InsertConsumerRequestSessionFailureKind.DatabaseFailure,
                "Database failure while inserting consumer request session.");
        }
    }

    public async Task<GetConsumerRequestSessionResult> GetConsumerRequestSessionAsync(
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
                    AuthenticatedTokenUsername,
                    CreatedUtc,
                    ClosedUtc
                from dbo.ConsumerRequestSessions
                where SessionId = @SessionId;
                """;

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = sessionId;

            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return GetConsumerRequestSessionResult.Failure(
                    GetConsumerRequestSessionFailureKind.NotFound,
                    "Consumer request session was not found.");
            }

            ConsumerRequestSessionData session = new(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetDateTime(4),
                reader.IsDBNull(5) ? null : reader.GetDateTime(5));

            return GetConsumerRequestSessionResult.Success(session);
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while retrieving consumer request session {SessionId}.", sessionId);
            return GetConsumerRequestSessionResult.Failure(
                GetConsumerRequestSessionFailureKind.DatabaseFailure,
                "Database failure while retrieving consumer request session.");
        }
    }

    public async Task<CloseConsumerRequestSessionResult> CloseConsumerRequestSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                update dbo.ConsumerRequestSessions
                   set SessionState = @ClosedState,
                       ClosedUtc = coalesce(ClosedUtc, sysutcdatetime())
                 where SessionId = @SessionId
                   and SessionState = @OpenState
                   and ClosedUtc is null;
                """;

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@ClosedState", SqlDbType.NVarChar, 32).Value = ConsumerRequestSessionStates.Closed;
            command.Parameters.Add("@OpenState", SqlDbType.NVarChar, 32).Value = ConsumerRequestSessionStates.Open;
            command.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = sessionId;

            int affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affectedRows == 0)
            {
                return CloseConsumerRequestSessionResult.Failure(
                    CloseConsumerRequestSessionFailureKind.NotFound,
                    "Consumer request session was not found.");
            }

            return CloseConsumerRequestSessionResult.Success();
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while closing consumer request session {SessionId}.", sessionId);
            return CloseConsumerRequestSessionResult.Failure(
                CloseConsumerRequestSessionFailureKind.DatabaseFailure,
                "Database failure while closing consumer request session.");
        }
    }
}
