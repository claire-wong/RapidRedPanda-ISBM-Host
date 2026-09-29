using Microsoft.Data.SqlClient;
using System.Data;

namespace RapidRedPanda.ISBM.Host.Data;

public sealed class SqlProviderPublicationSessionRepository : IProviderPublicationSessionRepository
{
    private const string ConnectionStringName = "ISBMHost";

    private readonly string _connectionString;
    private readonly ILogger<SqlProviderPublicationSessionRepository> _logger;

    public SqlProviderPublicationSessionRepository(
        IConfiguration configuration,
        ILogger<SqlProviderPublicationSessionRepository> logger)
    {
        _connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        _logger = logger;
    }

    public async Task<InsertProviderPublicationSessionResult> InsertProviderPublicationSessionAsync(
        ProviderPublicationSessionCreateData session,
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
                    insert dbo.ProviderPublicationSessions
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

                await using SqlCommand command = new(insertSessionSql, connection, transaction);
                command.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = session.SessionId;
                command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = session.ChannelId;
                command.Parameters.Add("@SessionState", SqlDbType.NVarChar, 32).Value = ProviderPublicationSessionStates.Open;
                command.Parameters.Add("@AuthenticatedTokenUsername", SqlDbType.NVarChar, 256).Value =
                    (object?)session.AuthenticatedTokenUsername ?? DBNull.Value;

                await command.ExecuteNonQueryAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await RollbackQuietlyAsync(transaction, cancellationToken);
                throw;
            }

            return InsertProviderPublicationSessionResult.Success();
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while inserting provider publication session {SessionId}.", session.SessionId);
            return InsertProviderPublicationSessionResult.Failure(
                InsertProviderPublicationSessionFailureKind.DatabaseFailure,
                "Database failure while inserting provider publication session.");
        }
    }

    public async Task<GetProviderPublicationSessionResult> GetProviderPublicationSessionAsync(
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
                from dbo.ProviderPublicationSessions
                where SessionId = @SessionId;
                """;

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = sessionId;

            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return GetProviderPublicationSessionResult.Failure(
                    GetProviderPublicationSessionFailureKind.NotFound,
                    "Provider publication session was not found.");
            }

            ProviderPublicationSessionData session = new(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetDateTime(4),
                reader.IsDBNull(5) ? null : reader.GetDateTime(5));

            return GetProviderPublicationSessionResult.Success(session);
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while retrieving provider publication session {SessionId}.", sessionId);
            return GetProviderPublicationSessionResult.Failure(
                GetProviderPublicationSessionFailureKind.DatabaseFailure,
                "Database failure while retrieving provider publication session.");
        }
    }

    public async Task<CloseProviderPublicationSessionResult> CloseProviderPublicationSessionAsync(
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
                    declare @ClosedUtc datetime2(7) = sysutcdatetime();

                    update dbo.ProviderPublicationSessions
                       set SessionState = @ClosedState,
                           ClosedUtc = @ClosedUtc
                     where SessionId = @SessionId
                       and SessionState = @OpenState
                       and ClosedUtc is null;

                    if @@rowcount = 0
                    begin
                        select cast(0 as bit);
                        return;
                    end;

                    update dbo.ProviderPublications
                       set ExpiredUtc = @ClosedUtc
                     where ProviderSessionId = @SessionId
                       and ExpiredUtc is null
                       and (EffectiveExpiryUtc is null or EffectiveExpiryUtc > @ClosedUtc);

                    select cast(1 as bit);
                    """;

                await using SqlCommand command = new(sql, connection, transaction);
                command.Parameters.Add("@ClosedState", SqlDbType.NVarChar, 32).Value = ProviderPublicationSessionStates.Closed;
                command.Parameters.Add("@OpenState", SqlDbType.NVarChar, 32).Value = ProviderPublicationSessionStates.Open;
                command.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = sessionId;

                bool closed = Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
                if (!closed)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return CloseProviderPublicationSessionResult.Failure(
                        CloseProviderPublicationSessionFailureKind.NotFound,
                        "Provider publication session was not found.");
                }

                await transaction.CommitAsync(cancellationToken);
                return CloseProviderPublicationSessionResult.Success();
            }
            catch
            {
                await RollbackQuietlyAsync(transaction, cancellationToken);
                throw;
            }
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while closing provider publication session {SessionId}.", sessionId);
            return CloseProviderPublicationSessionResult.Failure(
                CloseProviderPublicationSessionFailureKind.DatabaseFailure,
                "Database failure while closing provider publication session.");
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
