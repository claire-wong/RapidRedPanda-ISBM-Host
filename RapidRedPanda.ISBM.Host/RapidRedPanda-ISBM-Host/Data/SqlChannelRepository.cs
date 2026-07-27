using BCrypt.Net;
using Microsoft.Data.SqlClient;
using RapidRedPanda.ISBM.Host.Services;
using RapidRedPanda.ISBM.Host.Validation;
using System.Data;

namespace RapidRedPanda.ISBM.Host.Data;

public sealed class SqlChannelRepository : IChannelRepository
{
    private const string ConnectionStringName = "ISBMHost";
    private const string DuplicateChannelUriIndexName = "UQ_Channels_ChannelUri";
    private const string DuplicateTokenUsernameIndexName = "UQ_ChannelSecurityTokens_ChannelId_TokenType_NormalizedUsername";
    private const string UsernameTokenType = "UsernameToken";

    private readonly string _connectionString;
    private readonly ILogger<SqlChannelRepository> _logger;

    public SqlChannelRepository(IConfiguration configuration, ILogger<SqlChannelRepository> logger)
    {
        _connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        _logger = logger;
    }

    public async Task<IReadOnlyList<ChannelData>> GetChannelsAsync(ChannelCredentials? credentials, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        List<ChannelData> channels = [];

        const string unsecuredSql = """
            select c.ChannelId, c.ChannelUri, c.ChannelType, c.Description, c.AzureTopicName
            from dbo.Channels as c
            where c.DeletedUtc is null
              and not exists
              (
                  select 1
                  from dbo.ChannelSecurityTokens as st
                  where st.ChannelId = c.ChannelId
                    and st.DeletedUtc is null
              )
            order by c.CreatedUtc, c.ChannelUri;
            """;

        await using (SqlCommand command = new(unsecuredSql, connection))
        await using (SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                channels.Add(ReadChannel(reader));
            }
        }

        if (credentials is null)
        {
            return channels;
        }

        const string securedSql = """
            select c.ChannelId, c.ChannelUri, c.ChannelType, c.Description, c.AzureTopicName, st.PasswordHash
            from dbo.Channels as c
            inner join dbo.ChannelSecurityTokens as st
                on st.ChannelId = c.ChannelId
               and st.DeletedUtc is null
               and st.TokenType = N'UsernameToken'
               and st.NormalizedUsername = @NormalizedUsername
            where c.DeletedUtc is null
            order by c.CreatedUtc, c.ChannelUri;
            """;

        await using SqlCommand securedCommand = new(securedSql, connection);
        securedCommand.Parameters.Add("@NormalizedUsername", SqlDbType.NVarChar, ChannelValidationRules.UsernameMaxLength).Value = credentials.NormalizedUsername;

        HashSet<Guid> existingChannelIds = channels.Select(channel => channel.ChannelId).ToHashSet();

        await using SqlDataReader securedReader = await securedCommand.ExecuteReaderAsync(cancellationToken);
        while (await securedReader.ReadAsync(cancellationToken))
        {
            string passwordHash = securedReader.GetString(5);
            if (!BCrypt.Net.BCrypt.Verify(credentials.Password, passwordHash))
            {
                continue;
            }

            ChannelData channel = ReadChannel(securedReader);
            if (existingChannelIds.Add(channel.ChannelId))
            {
                channels.Add(channel);
            }
        }

        return channels;
    }

    public async Task<GetChannelResult> GetChannelAsync(string channelUri, CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(GetConnectionString());
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                select
                    c.ChannelId,
                    c.ChannelUri,
                    c.ChannelType,
                    c.Description,
                    c.AzureTopicName,
                    st.TokenType,
                    st.Username
                from dbo.Channels as c
                left join dbo.ChannelSecurityTokens as st
                    on st.ChannelId = c.ChannelId
                   and st.DeletedUtc is null
                where c.ChannelUri collate Latin1_General_100_CS_AS_SC = @ChannelUri collate Latin1_General_100_CS_AS_SC
                  and c.DeletedUtc is null
                order by st.CreatedUtc, st.Username;
                """;

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@ChannelUri", SqlDbType.NVarChar, ChannelValidationRules.ChannelUriMaxLength).Value = channelUri;

            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            ChannelData? channel = null;
            List<ChannelSecurityTokenData> securityTokens = [];

            while (await reader.ReadAsync(cancellationToken))
            {
                channel ??= ReadChannel(reader);

                if (!reader.IsDBNull(5) && !reader.IsDBNull(6))
                {
                    securityTokens.Add(new ChannelSecurityTokenData(
                        reader.GetString(5),
                        reader.GetString(6)));
                }
            }

            if (channel is null)
            {
                return GetChannelResult.Failure(GetChannelFailureKind.NotFound, "Channel was not found.");
            }

            return GetChannelResult.Success(new ChannelDetailData(
                channel.ChannelId,
                channel.ChannelUri,
                channel.ChannelType,
                channel.Description,
                channel.AzureTopicName,
                securityTokens));
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while retrieving channel.");
            return GetChannelResult.Failure(GetChannelFailureKind.DatabaseFailure, "Database failure while retrieving channel.");
        }
    }

    public async Task<GetChannelTopicBindingResult> GetChannelTopicBindingAsync(string channelUri, CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(GetConnectionString());
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                select ChannelId, ChannelUri, ChannelType, Description, AzureTopicName
                from dbo.Channels
                where ChannelUri collate Latin1_General_100_CS_AS_SC = @ChannelUri collate Latin1_General_100_CS_AS_SC
                  and DeletedUtc is null;
                """;

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@ChannelUri", SqlDbType.NVarChar, ChannelValidationRules.ChannelUriMaxLength).Value = channelUri;

            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return GetChannelTopicBindingResult.Failure(GetChannelTopicBindingFailureKind.NotFound, "Channel was not found.");
            }

            ChannelData channel = ReadChannel(reader);
            return GetChannelTopicBindingResult.Success(new ChannelTopicBindingData(
                channel.ChannelId,
                channel.ChannelUri,
                channel.ChannelType,
                channel.AzureTopicName));
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while retrieving channel topic binding.");
            return GetChannelTopicBindingResult.Failure(GetChannelTopicBindingFailureKind.DatabaseFailure, "Database failure while retrieving channel topic binding.");
        }
    }

    public async Task<GetChannelTopicBindingResult> GetChannelTopicBindingByIdAsync(Guid channelId, CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(GetConnectionString());
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                select ChannelId, ChannelUri, ChannelType, Description, AzureTopicName
                from dbo.Channels
                where ChannelId = @ChannelId
                  and DeletedUtc is null;
                """;

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = channelId;

            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return GetChannelTopicBindingResult.Failure(GetChannelTopicBindingFailureKind.NotFound, "Channel was not found.");
            }

            ChannelData channel = ReadChannel(reader);
            return GetChannelTopicBindingResult.Success(new ChannelTopicBindingData(
                channel.ChannelId,
                channel.ChannelUri,
                channel.ChannelType,
                channel.AzureTopicName));
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while retrieving channel topic binding by channel id.");
            return GetChannelTopicBindingResult.Failure(GetChannelTopicBindingFailureKind.DatabaseFailure, "Database failure while retrieving channel topic binding.");
        }
    }

    public async Task<CreateChannelResult> CreateChannelAsync(ChannelCreateRequestData request, CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(GetConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                const string insertChannelSql = """
                    insert dbo.Channels (ChannelId, ChannelUri, ChannelType, Description, AzureTopicName)
                    values (@ChannelId, @ChannelUri, @ChannelType, @Description, @AzureTopicName);
                    """;

                await using (SqlCommand command = new(insertChannelSql, connection, transaction))
                {
                    command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = request.ChannelId;
                    command.Parameters.Add("@ChannelUri", SqlDbType.NVarChar, ChannelValidationRules.ChannelUriMaxLength).Value = request.ChannelUri;
                    command.Parameters.Add("@ChannelType", SqlDbType.NVarChar, 32).Value = request.ChannelType;
                    command.Parameters.Add("@Description", SqlDbType.NVarChar, ChannelValidationRules.DescriptionMaxLength).Value = (object?)request.Description ?? DBNull.Value;
                    command.Parameters.Add("@AzureTopicName", SqlDbType.NVarChar, 260).Value = request.AzureTopicName;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                foreach (ChannelCreateToken token in request.SecurityTokens)
                {
                    const string insertTokenSql = """
                        insert dbo.ChannelSecurityTokens
                        (
                            ChannelSecurityTokenId,
                            ChannelId,
                            TokenType,
                            Username,
                            NormalizedUsername,
                            PasswordHash
                        )
                        values
                        (
                            @ChannelSecurityTokenId,
                            @ChannelId,
                            N'UsernameToken',
                            @Username,
                            @NormalizedUsername,
                            @PasswordHash
                        );
                        """;

                    await using SqlCommand command = new(insertTokenSql, connection, transaction);
                    command.Parameters.Add("@ChannelSecurityTokenId", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
                    command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = request.ChannelId;
                    command.Parameters.Add("@Username", SqlDbType.NVarChar, ChannelValidationRules.UsernameMaxLength).Value = token.Username;
                    command.Parameters.Add("@NormalizedUsername", SqlDbType.NVarChar, ChannelValidationRules.UsernameMaxLength).Value = token.NormalizedUsername;
                    command.Parameters.Add("@PasswordHash", SqlDbType.NVarChar, ChannelValidationRules.PasswordHashMaxLength).Value = BCrypt.Net.BCrypt.HashPassword(token.Password);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);

                ChannelData channel = new(request.ChannelId, request.ChannelUri, request.ChannelType, request.Description, request.AzureTopicName);
                return CreateChannelResult.Success(channel);
            }
            catch
            {
                await RollbackQuietlyAsync(transaction, cancellationToken);
                throw;
            }
        }
        catch (SqlException exception) when (IsUniqueViolation(exception, DuplicateChannelUriIndexName))
        {
            return CreateChannelResult.Failure(CreateChannelFailureKind.DuplicateChannelUri, "Channel URI already exists.");
        }
        catch (SqlException exception) when (IsUniqueViolation(exception, DuplicateTokenUsernameIndexName))
        {
            return CreateChannelResult.Failure(CreateChannelFailureKind.DuplicateTokenUsername, "Duplicate UsernameToken for the same channel.");
        }
        catch (SqlException exception) when (IsCheckViolation(exception, "CK_Channels_ChannelType"))
        {
            return CreateChannelResult.Failure(CreateChannelFailureKind.InvalidChannelType, "Invalid channel type.");
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while creating channel.");
            return CreateChannelResult.Failure(CreateChannelFailureKind.DatabaseFailure, "Database failure while creating channel.");
        }
    }

    public async Task<GetChannelForSubscriptionResult> GetChannelForConsumerPublicationSubscriptionAsync(
        string channelUri,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken)
    {
        return await GetChannelForSessionAsync(
            channelUri,
            credentials,
            "Publication",
            "Database failure while retrieving channel for consumer subscription session.",
            cancellationToken);
    }

    public async Task<GetChannelForSubscriptionResult> GetChannelForProviderPublicationSessionAsync(
        string channelUri,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken)
    {
        return await GetChannelForSessionAsync(
            channelUri,
            credentials,
            "Publication",
            "Database failure while retrieving channel for provider publication session.",
            cancellationToken);
    }

    public async Task<GetChannelForSubscriptionResult> GetChannelForProviderRequestSessionAsync(
        string channelUri,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken)
    {
        return await GetChannelForSessionAsync(
            channelUri,
            credentials,
            "Request",
            "Database failure while retrieving channel for provider request session.",
            cancellationToken);
    }

    public async Task<GetChannelForSubscriptionResult> GetChannelForConsumerRequestSessionAsync(
        string channelUri,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken)
    {
        return await GetChannelForSessionAsync(
            channelUri,
            credentials,
            "Request",
            "Database failure while retrieving channel for consumer request session.",
            cancellationToken);
    }

    private async Task<GetChannelForSubscriptionResult> GetChannelForSessionAsync(
        string channelUri,
        ChannelCredentials? credentials,
        string requiredChannelType,
        string databaseFailureLogMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(GetConnectionString());
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                select
                    c.ChannelId,
                    c.ChannelUri,
                    c.ChannelType,
                    c.Description,
                    c.AzureTopicName,
                    st.Username,
                    st.NormalizedUsername,
                    st.PasswordHash
                from dbo.Channels as c
                left join dbo.ChannelSecurityTokens as st
                    on st.ChannelId = c.ChannelId
                   and st.DeletedUtc is null
                   and st.TokenType = N'UsernameToken'
                where c.ChannelUri collate Latin1_General_100_CS_AS_SC = @ChannelUri collate Latin1_General_100_CS_AS_SC
                  and c.DeletedUtc is null
                order by st.CreatedUtc, st.Username;
                """;

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@ChannelUri", SqlDbType.NVarChar, ChannelValidationRules.ChannelUriMaxLength).Value = channelUri;

            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            ChannelData? channel = null;
            bool hasSecurityTokens = false;
            string? authenticatedUsername = null;

            while (await reader.ReadAsync(cancellationToken))
            {
                channel ??= ReadChannel(reader);

                if (reader.IsDBNull(5) || reader.IsDBNull(6) || reader.IsDBNull(7))
                {
                    continue;
                }

                hasSecurityTokens = true;

                if (credentials is null || authenticatedUsername is not null)
                {
                    continue;
                }

                string normalizedUsername = reader.GetString(6);
                if (!string.Equals(normalizedUsername, credentials.NormalizedUsername, StringComparison.Ordinal))
                {
                    continue;
                }

                string passwordHash = reader.GetString(7);
                if (BCrypt.Net.BCrypt.Verify(credentials.Password, passwordHash))
                {
                    authenticatedUsername = reader.GetString(5);
                }
            }

            if (channel is null)
            {
                return GetChannelForSubscriptionResult.Failure(GetChannelForSubscriptionFailureKind.NotFound, "Channel was not found.");
            }

            if (!string.Equals(channel.ChannelType, requiredChannelType, StringComparison.Ordinal))
            {
                return GetChannelForSubscriptionResult.Failure(GetChannelForSubscriptionFailureKind.InvalidChannelType, "Channel is not the required type.");
            }

            if (hasSecurityTokens && authenticatedUsername is null)
            {
                return GetChannelForSubscriptionResult.Failure(GetChannelForSubscriptionFailureKind.SecurityTokenInvalid, "Security token was not valid for the channel.");
            }

            return GetChannelForSubscriptionResult.Success(new ChannelSubscriptionData(
                channel.ChannelId,
                channel.ChannelUri,
                channel.ChannelType,
                channel.AzureTopicName,
                authenticatedUsername));
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, databaseFailureLogMessage);
            return GetChannelForSubscriptionResult.Failure(GetChannelForSubscriptionFailureKind.DatabaseFailure, "Database failure while retrieving channel.");
        }
    }

    public async Task<DeleteChannelResult> DeleteChannelAsync(string channelUri, CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(GetConnectionString());
            await connection.OpenAsync(cancellationToken);

            await using (SqlCommand command = new("set xact_abort on;", connection))
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                Guid? channelId = null;
                DateTime deletedUtc;

                const string selectSql = """
                    select ChannelId
                    from dbo.Channels with (updlock, holdlock)
                    where ChannelUri collate Latin1_General_100_CS_AS_SC = @ChannelUri collate Latin1_General_100_CS_AS_SC
                      and DeletedUtc is null;
                    """;

                await using (SqlCommand command = new(selectSql, connection, transaction))
                {
                    command.Parameters.Add("@ChannelUri", SqlDbType.NVarChar, ChannelValidationRules.ChannelUriMaxLength).Value = channelUri;
                    object? result = await command.ExecuteScalarAsync(cancellationToken);
                    if (result is Guid id)
                    {
                        channelId = id;
                    }
                }

                if (channelId is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return DeleteChannelResult.Failure(DeleteChannelFailureKind.NotFound, "Channel was not found.");
                }

                await using (SqlCommand command = new("select sysutcdatetime();", connection, transaction))
                {
                    deletedUtc = (DateTime)(await command.ExecuteScalarAsync(cancellationToken)
                        ?? throw new InvalidOperationException("SQL Server did not return a delete timestamp."));
                }

                const string softDeleteTokensSql = """
                    update dbo.ChannelSecurityTokens
                       set DeletedUtc = @DeletedUtc
                     where ChannelId = @ChannelId
                       and DeletedUtc is null;
                    """;

                await using (SqlCommand command = new(softDeleteTokensSql, connection, transaction))
                {
                    command.Parameters.Add("@DeletedUtc", SqlDbType.DateTime2).Value = deletedUtc;
                    command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = channelId.Value;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                const string softDeleteChannelSql = """
                    update dbo.Channels
                       set DeletedUtc = @DeletedUtc
                     where ChannelId = @ChannelId
                       and DeletedUtc is null;
                    """;

                await using (SqlCommand command = new(softDeleteChannelSql, connection, transaction))
                {
                    command.Parameters.Add("@DeletedUtc", SqlDbType.DateTime2).Value = deletedUtc;
                    command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = channelId.Value;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return DeleteChannelResult.Success();
            }
            catch
            {
                await RollbackQuietlyAsync(transaction, cancellationToken);
                throw;
            }
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while deleting channel.");
            return DeleteChannelResult.Failure(DeleteChannelFailureKind.DatabaseFailure, "Database failure while deleting channel.");
        }
    }

    public async Task<ValidateChannelCredentialsResult> ValidateChannelCredentialsAsync(
        Guid channelId,
        string? expectedAuthenticatedTokenUsername,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(GetConnectionString());
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                select st.Username, st.NormalizedUsername, st.PasswordHash
                from dbo.Channels as c
                left join dbo.ChannelSecurityTokens as st
                    on st.ChannelId = c.ChannelId
                   and st.DeletedUtc is null
                   and st.TokenType = N'UsernameToken'
                where c.ChannelId = @ChannelId
                  and c.DeletedUtc is null
                order by st.CreatedUtc, st.Username;
                """;

            await using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = channelId;

            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            bool foundChannel = false;
            bool hasSecurityTokens = false;

            while (await reader.ReadAsync(cancellationToken))
            {
                foundChannel = true;
                if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2))
                {
                    continue;
                }

                hasSecurityTokens = true;
                if (credentials is null ||
                    !string.Equals(reader.GetString(0), expectedAuthenticatedTokenUsername, StringComparison.Ordinal) ||
                    !string.Equals(reader.GetString(1), credentials.NormalizedUsername, StringComparison.Ordinal))
                {
                    continue;
                }

                if (BCrypt.Net.BCrypt.Verify(credentials.Password, reader.GetString(2)))
                {
                    return ValidateChannelCredentialsResult.Success();
                }
            }

            if (!foundChannel)
            {
                return ValidateChannelCredentialsResult.Failure(
                    ValidateChannelCredentialsFailureKind.NotFound,
                    "Channel was not found.");
            }

            return hasSecurityTokens
                ? ValidateChannelCredentialsResult.Failure(
                    ValidateChannelCredentialsFailureKind.SecurityTokenInvalid,
                    "Security token was not valid for the channel.")
                : ValidateChannelCredentialsResult.Success();
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while validating channel credentials. ChannelId: {ChannelId}.", channelId);
            return ValidateChannelCredentialsResult.Failure(
                ValidateChannelCredentialsFailureKind.DatabaseFailure,
                "Database failure while validating channel credentials.");
        }
    }

    public async Task<SecurityTokenMutationResult> AddSecurityTokensAsync(string channelUri, IReadOnlyList<ChannelCreateToken> tokens, CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(GetConnectionString());
            await connection.OpenAsync(cancellationToken);

            await using (SqlCommand command = new("set xact_abort on;", connection))
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                Guid? channelId = await FindActiveChannelIdAsync(connection, transaction, channelUri, cancellationToken);
                if (channelId is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return SecurityTokenMutationResult.Failure(SecurityTokenMutationFailureKind.NotFound, "Channel was not found.");
                }

                foreach (ChannelCreateToken token in tokens)
                {
                    const string insertSql = """
                        insert dbo.ChannelSecurityTokens
                        (
                            ChannelSecurityTokenId,
                            ChannelId,
                            TokenType,
                            Username,
                            NormalizedUsername,
                            PasswordHash
                        )
                        select
                            newid(),
                            @ChannelId,
                            @TokenType,
                            @Username,
                            @NormalizedUsername,
                            @PasswordHash
                        where not exists
                        (
                            select 1
                            from dbo.ChannelSecurityTokens with (updlock, holdlock)
                            where ChannelId = @ChannelId
                              and TokenType = @TokenType
                              and NormalizedUsername = @NormalizedUsername
                              and DeletedUtc is null
                        );
                        """;

                    await using SqlCommand command = new(insertSql, connection, transaction);
                    command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = channelId.Value;
                    command.Parameters.Add("@TokenType", SqlDbType.NVarChar, 64).Value = UsernameTokenType;
                    command.Parameters.Add("@Username", SqlDbType.NVarChar, ChannelValidationRules.UsernameMaxLength).Value = token.Username;
                    command.Parameters.Add("@NormalizedUsername", SqlDbType.NVarChar, ChannelValidationRules.UsernameMaxLength).Value = token.NormalizedUsername;
                    command.Parameters.Add("@PasswordHash", SqlDbType.NVarChar, ChannelValidationRules.PasswordHashMaxLength).Value = BCrypt.Net.BCrypt.HashPassword(token.Password);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return SecurityTokenMutationResult.Success();
            }
            catch
            {
                await RollbackQuietlyAsync(transaction, cancellationToken);
                throw;
            }
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while adding security tokens.");
            return SecurityTokenMutationResult.Failure(SecurityTokenMutationFailureKind.DatabaseFailure, "Database failure while adding security tokens.");
        }
    }

    public async Task<SecurityTokenMutationResult> RemoveSecurityTokensAsync(string channelUri, IReadOnlyList<ChannelCreateToken> tokens, CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = new(GetConnectionString());
            await connection.OpenAsync(cancellationToken);

            await using (SqlCommand command = new("set xact_abort on;", connection))
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                Guid? channelId = await FindActiveChannelIdAsync(connection, transaction, channelUri, cancellationToken);
                if (channelId is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return SecurityTokenMutationResult.Failure(SecurityTokenMutationFailureKind.NotFound, "Channel was not found.");
                }

                DateTime deletedUtc;
                await using (SqlCommand command = new("select sysutcdatetime();", connection, transaction))
                {
                    deletedUtc = (DateTime)(await command.ExecuteScalarAsync(cancellationToken)
                        ?? throw new InvalidOperationException("SQL Server did not return a delete timestamp."));
                }

                foreach (ChannelCreateToken token in tokens)
                {
                    const string updateSql = """
                        update dbo.ChannelSecurityTokens
                           set DeletedUtc = @DeletedUtc
                         where ChannelId = @ChannelId
                           and TokenType = @TokenType
                           and NormalizedUsername = @NormalizedUsername
                           and DeletedUtc is null;
                        """;

                    await using SqlCommand command = new(updateSql, connection, transaction);
                    command.Parameters.Add("@DeletedUtc", SqlDbType.DateTime2).Value = deletedUtc;
                    command.Parameters.Add("@ChannelId", SqlDbType.UniqueIdentifier).Value = channelId.Value;
                    command.Parameters.Add("@TokenType", SqlDbType.NVarChar, 64).Value = UsernameTokenType;
                    command.Parameters.Add("@NormalizedUsername", SqlDbType.NVarChar, ChannelValidationRules.UsernameMaxLength).Value = token.NormalizedUsername;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return SecurityTokenMutationResult.Success();
            }
            catch
            {
                await RollbackQuietlyAsync(transaction, cancellationToken);
                throw;
            }
        }
        catch (SqlException exception)
        {
            _logger.LogError(exception, "Database failure while removing security tokens.");
            return SecurityTokenMutationResult.Failure(SecurityTokenMutationFailureKind.DatabaseFailure, "Database failure while removing security tokens.");
        }
    }

    private string GetConnectionString()
    {
        return _connectionString;
    }

    private static ChannelData ReadChannel(SqlDataReader reader)
    {
        return new ChannelData(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4));
    }

    private static async Task<Guid?> FindActiveChannelIdAsync(SqlConnection connection, SqlTransaction transaction, string channelUri, CancellationToken cancellationToken)
    {
        const string selectSql = """
            select ChannelId
            from dbo.Channels with (updlock, holdlock)
            where ChannelUri collate Latin1_General_100_CS_AS_SC = @ChannelUri collate Latin1_General_100_CS_AS_SC
              and DeletedUtc is null;
            """;

        await using SqlCommand command = new(selectSql, connection, transaction);
        command.Parameters.Add("@ChannelUri", SqlDbType.NVarChar, ChannelValidationRules.ChannelUriMaxLength).Value = channelUri;
        object? result = await command.ExecuteScalarAsync(cancellationToken);

        return result is Guid id ? id : null;
    }

    private static bool IsUniqueViolation(SqlException exception, string objectName)
    {
        return exception.Errors.Cast<SqlError>().Any(error =>
            error.Number is 2601 or 2627 &&
            error.Message.Contains(objectName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCheckViolation(SqlException exception, string objectName)
    {
        return exception.Errors.Cast<SqlError>().Any(error =>
            error.Number == 547 &&
            error.Message.Contains(objectName, StringComparison.OrdinalIgnoreCase));
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
