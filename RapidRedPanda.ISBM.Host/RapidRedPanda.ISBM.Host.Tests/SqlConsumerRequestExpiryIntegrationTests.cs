using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Services;
using System.Data;

namespace RapidRedPanda.ISBM.Host.Tests;

[Trait("Category", "SqlIntegration")]
public sealed class SqlConsumerRequestExpiryIntegrationTests
{
    [SqlExpiryFact]
    public async Task ExpireOwnedActiveRequest_SuppressesOnlyEligibleRequestNotifications()
    {
        await using ExpirySqlFixture fixture = await ExpirySqlFixture.CreateAsync();
        string unrelatedBefore = await fixture.SnapshotAsync(unrelatedOnly: true);

        ConsumerRequestRepositoryResult result = await fixture.ExpireAsync();

        Assert.True(result.Succeeded);
        Assert.NotNull(await fixture.ExpiredUtcAsync());
        Assert.Equal(unrelatedBefore, await fixture.SnapshotAsync(unrelatedOnly: true));
        await fixture.AssertSuppressedAsync(fixture.PendingNotification);
        await fixture.AssertSuppressedAsync(fixture.RetryNotification);
        Assert.Equal("Delivered", await fixture.NotificationStateAsync(fixture.DeliveredNotification));
        Assert.Equal("Pending", await fixture.NotificationStateAsync(fixture.ResponseNotification));
    }

    [SqlExpiryTheory]
    [InlineData("ForeignSession")]
    [InlineData("ForeignChannel")]
    [InlineData("UnknownMessage")]
    [InlineData("ExplicitlyExpired")]
    [InlineData("TimedExpired")]
    [InlineData("AtDeadline")]
    public async Task ExpireNonmatchingOrExpiredRequest_IsNoOp(string scenario)
    {
        await using ExpirySqlFixture fixture = await ExpirySqlFixture.CreateAsync();
        if (scenario == "ExplicitlyExpired")
            await fixture.ExecuteAsync("update dbo.ConsumerRequests set ExpiredUtc = dateadd(minute, -1, sysutcdatetime()) where MessageId = @MessageId;");
        if (scenario == "TimedExpired")
            await fixture.ExecuteAsync("update dbo.ConsumerRequests set EffectiveExpiryUtc = dateadd(minute, -1, sysutcdatetime()) where MessageId = @MessageId;");
        if (scenario == "AtDeadline")
            await fixture.ExecuteAsync("update dbo.ConsumerRequests set EffectiveExpiryUtc = sysutcdatetime() where MessageId = @MessageId;");

        string before = await fixture.SnapshotAsync();
        Guid session = scenario switch
        {
            "ForeignSession" => fixture.OtherRequester,
            "ForeignChannel" => fixture.OtherChannelRequester,
            _ => fixture.Requester
        };
        Guid channel = scenario == "ForeignChannel" ? fixture.OtherChannel : fixture.Channel;
        Guid message = scenario == "UnknownMessage" ? Guid.NewGuid() : fixture.Message;

        ConsumerRequestRepositoryResult result = await fixture.Repository.ExpireRequestAsync(
            session, channel, message, DateTime.UtcNow, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [SqlExpiryTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpireActiveRequest_WithAbsentOrFutureDeadline_Succeeds(bool futureDeadline)
    {
        await using ExpirySqlFixture fixture = await ExpirySqlFixture.CreateAsync();
        if (futureDeadline)
            await fixture.ExecuteAsync("update dbo.ConsumerRequests set EffectiveExpiryUtc = dateadd(hour, 1, sysutcdatetime()) where MessageId = @MessageId;");

        Assert.True((await fixture.ExpireAsync()).Succeeded);
        await fixture.AssertSuppressedAsync(fixture.PendingNotification);
    }

    [SqlExpiryFact]
    public async Task RepeatExpiry_DoesNotSuppressStaleNotificationsOrChangeRowVersions()
    {
        await using ExpirySqlFixture fixture = await ExpirySqlFixture.CreateAsync();
        Assert.True((await fixture.ExpireAsync()).Succeeded);
        // Seed a stale notice to distinguish a no-op from another cleanup pass.
        await fixture.ExecuteAsync("update dbo.Notifications set State = N'Pending', ResolvedUtc = null, ResolutionReason = null where NotificationId = @PendingNotification;");
        string before = await fixture.SnapshotAsync();

        Assert.True((await fixture.ExpireAsync()).Succeeded);

        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [SqlExpiryFact]
    public async Task ConcurrentExpiry_PreservesExpiredStateAndRepeatNoOp()
    {
        await using ExpirySqlFixture fixture = await ExpirySqlFixture.CreateAsync();
        ConsumerRequestRepositoryResult[] results = await Task.WhenAll(fixture.ExpireAsync(), fixture.ExpireAsync());
        Assert.All(results, result => Assert.True(result.Succeeded));
        await fixture.AssertSuppressedAsync(fixture.PendingNotification);
        string beforeRepeat = await fixture.SnapshotAsync();

        Assert.True((await fixture.ExpireAsync()).Succeeded);
        Assert.Equal(beforeRepeat, await fixture.SnapshotAsync());
    }

    [SqlExpiryFact]
    public async Task SuppressionFailure_RollsBackRequestAndNotificationUpdates()
    {
        await using ExpirySqlFixture fixture = await ExpirySqlFixture.CreateAsync();
        await fixture.InstallSuppressionFailureAsync();
        string before = await fixture.SnapshotAsync();

        ConsumerRequestRepositoryResult result = await fixture.ExpireAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(ConsumerRequestRepositoryFailureKind.DatabaseFailure, result.FailureKind);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [SqlExpiryFact]
    public async Task ExpireWithoutEligibleNotifications_StillExpiresRequest()
    {
        await using ExpirySqlFixture fixture = await ExpirySqlFixture.CreateAsync();
        await fixture.ExecuteAsync("update dbo.Notifications set State = N'Delivered' where NotificationId in (@PendingNotification, @RetryNotification);");

        Assert.True((await fixture.ExpireAsync()).Succeeded);
        Assert.NotNull(await fixture.ExpiredUtcAsync());
        Assert.Equal("Delivered", await fixture.NotificationStateAsync(fixture.PendingNotification));
    }

    private sealed class ExpirySqlFixture : IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly string _failureTrigger = "ExpiryTest_" + Guid.NewGuid().ToString("N");
        private readonly Guid[] _responders = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        private readonly Guid _unrelatedMessage = Guid.NewGuid();

        public Guid Channel { get; } = Guid.NewGuid();
        public Guid OtherChannel { get; } = Guid.NewGuid();
        public Guid Requester { get; } = Guid.NewGuid();
        public Guid OtherRequester { get; } = Guid.NewGuid();
        public Guid OtherChannelRequester { get; } = Guid.NewGuid();
        public Guid Message { get; } = Guid.NewGuid();
        public Guid PendingNotification { get; } = Guid.NewGuid();
        public Guid RetryNotification { get; } = Guid.NewGuid();
        public Guid DeliveredNotification { get; } = Guid.NewGuid();
        public Guid ResponseNotification { get; } = Guid.NewGuid();
        public SqlConsumerRequestRepository Repository { get; }

        private ExpirySqlFixture()
        {
            if (Environment.GetEnvironmentVariable("RRP_ISBM_EXPIRY_SQL_TESTS_APPROVED") != "1")
                throw new InvalidOperationException("SQL fixture execution requires separate explicit approval.");

            string connectionString = Environment.GetEnvironmentVariable("RRP_ISBM_EXPIRY_SQL_CONNECTION_STRING")
                ?? throw new InvalidOperationException("The approved SQL fixture connection is not configured.");
            SqlConnectionStringBuilder builder = new(connectionString);
            if (!builder.InitialCatalog.StartsWith("RRP_ExpiryTests_", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Use an initialized disposable database named RRP_ExpiryTests_<suffix>.");
            builder.ApplicationName = "RapidRedPanda.ExpirySqlTests";
            builder.Enlist = false;
            _connectionString = builder.ConnectionString;
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:ISBMHost"] = _connectionString }).Build();
            Repository = new SqlConsumerRequestRepository(configuration,
                Options.Create(new PublicationNotificationOptions()), NullLogger<SqlConsumerRequestRepository>.Instance);
        }

        public static async Task<ExpirySqlFixture> CreateAsync()
        {
            ExpirySqlFixture fixture = new();
            // Use the real, previously initialized Host schema; never create a database here.
            await fixture.ExecuteAsync("""
                set xact_abort on;
                set nocount on;
                begin transaction;
                insert dbo.Channels (ChannelId, ChannelUri, ChannelType, AzureTopicName)
                values (@Channel, concat(N'urn:expiry-test:', @Channel), N'Request', convert(nvarchar(36), @Channel)),
                       (@OtherChannel, concat(N'urn:expiry-test:', @OtherChannel), N'Request', convert(nvarchar(36), @OtherChannel));
                insert dbo.ConsumerRequestSessions (SessionId, ChannelId, SessionState)
                values (@Requester, @Channel, N'Open'), (@OtherRequester, @Channel, N'Open'),
                       (@OtherChannelRequester, @OtherChannel, N'Open');
                insert dbo.ProviderRequestSessions (SessionId, ChannelId, SessionState, AzureTopicName, AzureSubscriptionName)
                values (@Responder0, @Channel, N'Open', convert(nvarchar(36), @Channel), convert(nvarchar(36), @Responder0)),
                       (@Responder1, @Channel, N'Open', convert(nvarchar(36), @Channel), convert(nvarchar(36), @Responder1)),
                       (@Responder2, @Channel, N'Open', convert(nvarchar(36), @Channel), convert(nvarchar(36), @Responder2));
                insert dbo.ConsumerRequests (MessageId, ConsumerSessionId, ChannelId, CreatedUtc)
                values (@MessageId, @Requester, @Channel, dateadd(hour, -1, sysutcdatetime())),
                       (@UnrelatedMessage, @OtherRequester, @Channel, dateadd(hour, -1, sysutcdatetime()));
                insert dbo.Notifications
                    (NotificationId, NotificationServiceType, MessageId, ProviderRequestSessionId, ListenerUrl, State, DeliveryDeadlineUtc, LastError)
                values (@PendingNotification, N'Request', @MessageId, @Responder0, N'http://localhost/unused', N'Pending', dateadd(day, 1, sysutcdatetime()), N'fixture'),
                       (@RetryNotification, N'Request', @MessageId, @Responder1, N'http://localhost/unused', N'RetryScheduled', dateadd(day, 1, sysutcdatetime()), N'fixture'),
                       (@DeliveredNotification, N'Request', @MessageId, @Responder2, N'http://localhost/unused', N'Delivered', dateadd(day, 1, sysutcdatetime()), null),
                       (newid(), N'Request', @UnrelatedMessage, @Responder0, N'http://localhost/unused', N'Pending', dateadd(day, 1, sysutcdatetime()), N'unrelated');
                insert dbo.Notifications
                    (NotificationId, NotificationServiceType, MessageId, RequestMessageId, ConsumerRequestSessionId, ListenerUrl, State, DeliveryDeadlineUtc)
                values (@ResponseNotification, N'Response', newid(), @MessageId, @Requester, N'http://localhost/unused', N'Pending', dateadd(day, 1, sysutcdatetime()));
                commit transaction;
                """);
            return fixture;
        }

        public Task<ConsumerRequestRepositoryResult> ExpireAsync() =>
            Repository.ExpireRequestAsync(Requester, Channel, Message, DateTime.UtcNow, CancellationToken.None);

        public async Task ExecuteAsync(string sql)
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync();
            await using SqlCommand command = CreateCommand(connection, sql);
            await command.ExecuteNonQueryAsync();
        }

        private SqlCommand CreateCommand(SqlConnection connection, string sql)
        {
            SqlCommand command = new(sql, connection);
            (string Name, Guid Value)[] values =
            [
                ("@Channel", Channel), ("@OtherChannel", OtherChannel),
                ("@Requester", Requester), ("@OtherRequester", OtherRequester),
                ("@OtherChannelRequester", OtherChannelRequester), ("@MessageId", Message),
                ("@UnrelatedMessage", _unrelatedMessage), ("@Responder0", _responders[0]),
                ("@Responder1", _responders[1]), ("@Responder2", _responders[2]),
                ("@PendingNotification", PendingNotification), ("@RetryNotification", RetryNotification),
                ("@DeliveredNotification", DeliveredNotification), ("@ResponseNotification", ResponseNotification)
            ];
            foreach ((string name, Guid value) in values)
                command.Parameters.Add(name, SqlDbType.UniqueIdentifier).Value = value;
            return command;
        }

        public async Task<string> SnapshotAsync(bool unrelatedOnly = false)
        {
            string filter = unrelatedOnly ? "MessageId = @UnrelatedMessage" : "MessageId in (@MessageId, @UnrelatedMessage)";
            string noticeFilter = unrelatedOnly
                ? "MessageId = @UnrelatedMessage"
                : "ProviderRequestSessionId in (@Responder0, @Responder1, @Responder2) or ConsumerRequestSessionId = @Requester";
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync();
            await using SqlCommand command = CreateCommand(connection, $"""
                select (select * from dbo.ConsumerRequests where {filter} order by MessageId for json path, include_null_values)
                    + char(10)
                    + (select * from dbo.Notifications where {noticeFilter} order by NotificationId for json path, include_null_values);
                """);
            return (string)(await command.ExecuteScalarAsync())!;
        }

        public async Task<DateTime?> ExpiredUtcAsync()
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync();
            await using SqlCommand command = CreateCommand(connection, "select ExpiredUtc from dbo.ConsumerRequests where MessageId = @MessageId;");
            object? value = await command.ExecuteScalarAsync();
            return value is DateTime timestamp ? timestamp : null;
        }

        public async Task<string> NotificationStateAsync(Guid notificationId)
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync();
            await using SqlCommand command = new("select State from dbo.Notifications where NotificationId = @Id;", connection);
            command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = notificationId;
            return (string)(await command.ExecuteScalarAsync())!;
        }

        public async Task AssertSuppressedAsync(Guid notificationId)
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync();
            await using SqlCommand command = new("""
                select State, ResolutionReason, NextAttemptUtc, LastError, ResolvedUtc
                from dbo.Notifications where NotificationId = @Id;
                """, connection);
            command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = notificationId;
            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("Suppressed", reader.GetString(0));
            Assert.Equal("RequestExpired", reader.GetString(1));
            Assert.True(reader.IsDBNull(2));
            Assert.True(reader.IsDBNull(3));
            Assert.False(reader.IsDBNull(4));
        }

        public Task InstallSuppressionFailureAsync() => ExecuteAsync($"""
            create trigger dbo.[{_failureTrigger}] on dbo.Notifications after update as
            begin
                set nocount on;
                if exists (select 1 from inserted where MessageId = '{Message:D}' and State = N'Suppressed')
                    throw 51098, 'Controlled expiry test suppression failure.', 1;
            end;
            """);

        public async ValueTask DisposeAsync()
        {
            await ExecuteAsync($"""
                drop trigger if exists dbo.[{_failureTrigger}];
                set xact_abort on;
                begin transaction;
                delete dbo.NotificationTopics where NotificationId in
                    (select NotificationId from dbo.Notifications where ProviderRequestSessionId in (@Responder0, @Responder1, @Responder2)
                        or ConsumerRequestSessionId = @Requester);
                delete dbo.Notifications where ProviderRequestSessionId in (@Responder0, @Responder1, @Responder2) or ConsumerRequestSessionId = @Requester;
                delete dbo.ConsumerRequests where MessageId in (@MessageId, @UnrelatedMessage);
                delete dbo.ProviderRequestSessions where SessionId in (@Responder0, @Responder1, @Responder2);
                delete dbo.ConsumerRequestSessions where SessionId in (@Requester, @OtherRequester, @OtherChannelRequester);
                delete dbo.Channels where ChannelId in (@Channel, @OtherChannel);
                commit transaction;
                """);
        }
    }
}

public sealed class SqlExpiryFactAttribute : FactAttribute
{
    public SqlExpiryFactAttribute() => Skip = SqlExpiryTestGate.SkipReason;
}

public sealed class SqlExpiryTheoryAttribute : TheoryAttribute
{
    public SqlExpiryTheoryAttribute() => Skip = SqlExpiryTestGate.SkipReason;
}

internal static class SqlExpiryTestGate
{
    public static string? SkipReason =>
        Environment.GetEnvironmentVariable("RRP_ISBM_EXPIRY_SQL_TESTS_APPROVED") == "1" &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RRP_ISBM_EXPIRY_SQL_CONNECTION_STRING"))
            ? null
            : "SQL execution requires separate approval and an initialized disposable RRP_ExpiryTests_<suffix> database.";
}

