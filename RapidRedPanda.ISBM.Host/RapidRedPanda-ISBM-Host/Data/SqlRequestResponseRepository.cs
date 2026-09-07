using Microsoft.Data.SqlClient;
using System.Data;

namespace RapidRedPanda.ISBM.Host.Data;

public sealed class SqlRequestResponseRepository : IRequestResponseRepository
{
    private const string ConnectionStringName = "ISBMHost";

    private readonly string _connectionString;
    private readonly ILogger<SqlRequestResponseRepository> _logger;

    public SqlRequestResponseRepository(
        IConfiguration configuration,
        ILogger<SqlRequestResponseRepository> logger)
    {
        _connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
        _logger = logger;
    }

    public async Task<ActiveRequestMatchResult> FindActiveRequestAsync(
        Guid requestMessageId,
        Guid channelId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                select ConsumerSessionId
                from dbo.ConsumerRequests
                where MessageId = @RequestMessageId
                  and ChannelId = @ChannelId
                  and ExpiredUtc is null
                  and (EffectiveExpiryUtc is null or EffectiveExpiryUtc > sysutcdatetime());
                """;

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@RequestMessageId", SqlDbType.UniqueIdentifier).Value = requestMessageId;
            command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = channelId;

            object? value = await command.ExecuteScalarAsync(cancellationToken);
            return value is Guid consumerSessionId
                ? ActiveRequestMatchResult.Success(consumerSessionId)
                : ActiveRequestMatchResult.NoMatch();
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while matching active request. RequestMessageId: {RequestMessageId}. ChannelId: {ChannelId}.", requestMessageId, channelId);
            return ActiveRequestMatchResult.Failure(
                RequestResponseRepositoryFailureKind.DatabaseFailure,
                "Database failure while matching active request.");
        }
    }

    public async Task<RequestResponseRepositoryResult> InsertResponseAsync(
        RequestResponseCreateData response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                insert dbo.RequestResponses
                (
                    MessageId,
                    RequestMessageId,
                    ConsumerRequestSessionId,
                    ProviderRequestSessionId,
                    ChannelId,
                    MessageContent,
                    MediaType,
                    Payload,
                    PayloadKind,
                    ContentEncoding,
                    CreatedUtc
                )
                values
                (
                    @MessageId,
                    @RequestMessageId,
                    @ConsumerRequestSessionId,
                    @ProviderRequestSessionId,
                    @ChannelId,
                    @MessageContent,
                    @MediaType,
                    @Payload,
                    @PayloadKind,
                    @ContentEncoding,
                    @CreatedUtc
                );
                """;

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@MessageId", SqlDbType.UniqueIdentifier).Value = response.MessageId;
            command.Parameters.Add("@RequestMessageId", SqlDbType.UniqueIdentifier).Value = response.RequestMessageId;
            command.Parameters.Add("@ConsumerRequestSessionId", SqlDbType.UniqueIdentifier).Value = response.ConsumerRequestSessionId;
            command.Parameters.Add("@ProviderRequestSessionId", SqlDbType.UniqueIdentifier).Value = response.ProviderRequestSessionId;
            command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = response.ChannelId;
            command.Parameters.Add("@MessageContent", SqlDbType.NVarChar, -1).Value = response.MessageContent.LegacyMessageContent;
            command.Parameters.Add("@MediaType", SqlDbType.NVarChar, 256).Value = (object?)response.MessageContent.MediaType ?? DBNull.Value;
            command.Parameters.Add("@Payload", SqlDbType.VarBinary, -1).Value = (object?)response.MessageContent.Payload ?? DBNull.Value;
            command.Parameters.Add("@PayloadKind", SqlDbType.NVarChar, 16).Value = (object?)response.MessageContent.PayloadKind ?? DBNull.Value;
            command.Parameters.Add("@ContentEncoding", SqlDbType.NVarChar, 64).Value = (object?)response.MessageContent.ContentEncoding ?? DBNull.Value;
            command.Parameters.Add("@CreatedUtc", SqlDbType.DateTime2).Value = response.CreatedUtc;
            await command.ExecuteNonQueryAsync(cancellationToken);

            return RequestResponseRepositoryResult.Success();
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while inserting response. MessageId: {MessageId}. RequestMessageId: {RequestMessageId}.", response.MessageId, response.RequestMessageId);
            return RequestResponseRepositoryResult.Failure(
                RequestResponseRepositoryFailureKind.DatabaseFailure,
                "Database failure while inserting response.");
        }
    }

    public async Task<ReadRequestResponseResult> ReadResponseAsync(
        Guid consumerRequestSessionId,
        Guid requestMessageId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            try
            {
                if (!await RequestBelongsToSessionAsync(connection, transaction, consumerRequestSessionId, requestMessageId, cancellationToken))
                {
                    await transaction.CommitAsync(cancellationToken);
                    return ReadRequestResponseResult.Failure(
                        RequestResponseRepositoryFailureKind.RequestNotFound,
                        "Request does not belong to the consumer request session.");
                }

                RequestResponseData? outstanding = await GetOutstandingResponseAsync(
                    connection,
                    transaction,
                    consumerRequestSessionId,
                    requestMessageId,
                    cancellationToken);
                if (outstanding is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return ReadRequestResponseResult.Success(outstanding);
                }

                RequestResponseData? next = await GetNextUnreadResponseAsync(
                    connection,
                    transaction,
                    consumerRequestSessionId,
                    requestMessageId,
                    cancellationToken);
                if (next is null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return ReadRequestResponseResult.NoResponse();
                }

                const string insertOutstandingSql = """
                    insert dbo.OutstandingRequestResponses
                    (
                        ConsumerRequestSessionId,
                        RequestMessageId,
                        RequestResponseId,
                        ReadUtc
                    )
                    values
                    (
                        @ConsumerRequestSessionId,
                        @RequestMessageId,
                        @RequestResponseId,
                        sysutcdatetime()
                    );
                    """;

                await using (SqlCommand command = new(insertOutstandingSql, connection, transaction))
                {
                    command.Parameters.Add("@ConsumerRequestSessionId", SqlDbType.UniqueIdentifier).Value = consumerRequestSessionId;
                    command.Parameters.Add("@RequestMessageId", SqlDbType.UniqueIdentifier).Value = requestMessageId;
                    command.Parameters.Add("@RequestResponseId", SqlDbType.BigInt).Value = next.RequestResponseId;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return ReadRequestResponseResult.Success(next);
            }
            catch
            {
                await RollbackQuietlyAsync(transaction, cancellationToken);
                throw;
            }
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while reading response. ConsumerRequestSessionId: {ConsumerRequestSessionId}. RequestMessageId: {RequestMessageId}.", consumerRequestSessionId, requestMessageId);
            return ReadRequestResponseResult.Failure(
                RequestResponseRepositoryFailureKind.DatabaseFailure,
                "Database failure while reading response.");
        }
    }

    public async Task<RequestResponseRepositoryResult> RemoveOutstandingResponseAsync(
        Guid consumerRequestSessionId,
        Guid requestMessageId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                if (!await RequestBelongsToSessionAsync(connection, transaction, consumerRequestSessionId, requestMessageId, cancellationToken))
                {
                    await transaction.CommitAsync(cancellationToken);
                    return RequestResponseRepositoryResult.Failure(
                        RequestResponseRepositoryFailureKind.RequestNotFound,
                        "Request does not belong to the consumer request session.");
                }

                const string updateSql = """
                    update response
                       set RemovedUtc = coalesce(response.RemovedUtc, sysutcdatetime())
                    from dbo.RequestResponses response
                    inner join dbo.OutstandingRequestResponses outstanding
                            on outstanding.RequestResponseId = response.RequestResponseId
                    where outstanding.ConsumerRequestSessionId = @ConsumerRequestSessionId
                      and outstanding.RequestMessageId = @RequestMessageId
                      and response.RemovedUtc is null;

                    delete dbo.OutstandingRequestResponses
                    where ConsumerRequestSessionId = @ConsumerRequestSessionId
                      and RequestMessageId = @RequestMessageId;
                    """;

                await using SqlCommand command = new(updateSql, connection, transaction);
                command.Parameters.Add("@ConsumerRequestSessionId", SqlDbType.UniqueIdentifier).Value = consumerRequestSessionId;
                command.Parameters.Add("@RequestMessageId", SqlDbType.UniqueIdentifier).Value = requestMessageId;
                await command.ExecuteNonQueryAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                return RequestResponseRepositoryResult.Success();
            }
            catch
            {
                await RollbackQuietlyAsync(transaction, cancellationToken);
                throw;
            }
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while removing response. ConsumerRequestSessionId: {ConsumerRequestSessionId}. RequestMessageId: {RequestMessageId}.", consumerRequestSessionId, requestMessageId);
            return RequestResponseRepositoryResult.Failure(
                RequestResponseRepositoryFailureKind.DatabaseFailure,
                "Database failure while removing response.");
        }
    }

    private static async Task<bool> RequestBelongsToSessionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid consumerRequestSessionId,
        Guid requestMessageId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select case when exists
            (
                select 1
                from dbo.ConsumerRequests with (updlock, holdlock)
                where MessageId = @RequestMessageId
                  and ConsumerSessionId = @ConsumerRequestSessionId
            )
            then cast(1 as bit) else cast(0 as bit) end;
            """;

        await using SqlCommand command = new(sql, connection, transaction);
        command.Parameters.Add("@ConsumerRequestSessionId", SqlDbType.UniqueIdentifier).Value = consumerRequestSessionId;
        command.Parameters.Add("@RequestMessageId", SqlDbType.UniqueIdentifier).Value = requestMessageId;
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<RequestResponseData?> GetOutstandingResponseAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid consumerRequestSessionId,
        Guid requestMessageId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select top (1)
                   response.RequestResponseId,
                   response.MessageId,
                   response.RequestMessageId,
                   response.ConsumerRequestSessionId,
                   response.ChannelId,
                   response.MessageContent,
                   response.MediaType,
                   response.Payload,
                   response.PayloadKind,
                   response.ContentEncoding
            from dbo.OutstandingRequestResponses outstanding with (updlock, holdlock)
            inner join dbo.RequestResponses response
                    on response.RequestResponseId = outstanding.RequestResponseId
            where outstanding.ConsumerRequestSessionId = @ConsumerRequestSessionId
              and outstanding.RequestMessageId = @RequestMessageId
              and response.RemovedUtc is null;
            """;

        await using SqlCommand command = new(sql, connection, transaction);
        command.Parameters.Add("@ConsumerRequestSessionId", SqlDbType.UniqueIdentifier).Value = consumerRequestSessionId;
        command.Parameters.Add("@RequestMessageId", SqlDbType.UniqueIdentifier).Value = requestMessageId;
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await ReadResponseDataAsync(reader, cancellationToken);
    }

    private static async Task<RequestResponseData?> GetNextUnreadResponseAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid consumerRequestSessionId,
        Guid requestMessageId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select top (1)
                   response.RequestResponseId,
                   response.MessageId,
                   response.RequestMessageId,
                   response.ConsumerRequestSessionId,
                   response.ChannelId,
                   response.MessageContent,
                   response.MediaType,
                   response.Payload,
                   response.PayloadKind,
                   response.ContentEncoding
            from dbo.RequestResponses response with (updlock, holdlock)
            inner join dbo.ConsumerRequests request
                    on request.MessageId = response.RequestMessageId
                   and request.ConsumerSessionId = response.ConsumerRequestSessionId
            where response.ConsumerRequestSessionId = @ConsumerRequestSessionId
              and response.RequestMessageId = @RequestMessageId
              and response.RemovedUtc is null
              and request.ExpiredUtc is null
              and (request.EffectiveExpiryUtc is null or request.EffectiveExpiryUtc > sysutcdatetime())
              and not exists
              (
                  select 1
                  from dbo.OutstandingRequestResponses outstanding
                  where outstanding.RequestResponseId = response.RequestResponseId
              )
            order by response.RequestResponseId;
            """;

        await using SqlCommand command = new(sql, connection, transaction);
        command.Parameters.Add("@ConsumerRequestSessionId", SqlDbType.UniqueIdentifier).Value = consumerRequestSessionId;
        command.Parameters.Add("@RequestMessageId", SqlDbType.UniqueIdentifier).Value = requestMessageId;
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await ReadResponseDataAsync(reader, cancellationToken);
    }

    private static async Task<RequestResponseData?> ReadResponseDataAsync(
        SqlDataReader reader,
        CancellationToken cancellationToken)
    {
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new RequestResponseData(
            reader.GetInt64(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetGuid(4),
            new PersistedMessageContent(
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : (byte[])reader.GetValue(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
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
