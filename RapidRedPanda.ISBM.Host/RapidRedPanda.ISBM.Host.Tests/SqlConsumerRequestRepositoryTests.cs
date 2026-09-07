namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class SqlConsumerRequestRepositoryTests
{
    [Fact]
    public async Task InsertPostedRequestSource_CreatesRequestNotificationsInsideRequestTransaction()
    {
        string source = await ReadHostFileAsync("Data", "SqlConsumerRequestRepository.cs");

        Assert.Contains("await connection.BeginTransactionAsync", source, StringComparison.Ordinal);
        Assert.Contains("insert dbo.ConsumerRequests", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("insert dbo.ConsumerRequestTopics", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("await InsertRequestNotificationsAsync(", source, StringComparison.Ordinal);
        Assert.Contains("await transaction.CommitAsync", source, StringComparison.Ordinal);
        Assert.Contains("NotificationTopics", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestNotificationSource_UsesExpectedRecipientEligibility()
    {
        string source = await ReadHostFileAsync("Data", "SqlConsumerRequestRepository.cs");

        Assert.Contains("session.ChannelId = @ChannelId", source, StringComparison.Ordinal);
        Assert.Contains("from dbo.ProviderRequestSessions session", source, StringComparison.Ordinal);
        Assert.Contains("session.SessionState = @OpenSessionState", source, StringComparison.Ordinal);
        Assert.Contains("session.OpenedUtc <= @CreatedUtc", source, StringComparison.Ordinal);
        Assert.Contains("session.ListenerUrl is not null", source, StringComparison.Ordinal);
        Assert.Contains("ltrim(rtrim(session.ListenerUrl)) <> N''", source, StringComparison.Ordinal);
        Assert.Contains("requestTopic.TopicValue = sessionTopic.TopicValue", source, StringComparison.Ordinal);
        Assert.Contains("requestTopic.MessageId = @MessageId", source, StringComparison.Ordinal);
        Assert.Contains("exists", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReadRequestAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveRequestAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestNotificationSource_DoesNotCreatePollingOnlyOrPayloadNotifications()
    {
        string source = await ReadHostFileAsync("Data", "SqlConsumerRequestRepository.cs");

        Assert.Contains("session.ListenerUrl is not null", source, StringComparison.Ordinal);
        Assert.Contains("ltrim(rtrim(session.ListenerUrl)) <> N''", source, StringComparison.Ordinal);
        Assert.Contains("select", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("session.SessionId", source, StringComparison.Ordinal);
        Assert.Contains("session.ListenerUrl", source, StringComparison.Ordinal);
        Assert.Contains("cross join dbo.ConsumerRequestTopics topic", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("topic.TopicValue", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageContent", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RequestContent", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequestNotificationSource_InsertsRequestShapedPendingRowsAndTopicSnapshots()
    {
        string source = await ReadHostFileAsync("Data", "SqlConsumerRequestRepository.cs");

        Assert.Contains("NotificationServiceType", source, StringComparison.Ordinal);
        Assert.Contains("ProviderRequestSessionId", source, StringComparison.Ordinal);
        Assert.Contains("ConsumerSubscriptionSessionId", source, StringComparison.Ordinal);
        Assert.Contains("ConsumerRequestSessionId", source, StringComparison.Ordinal);
        Assert.Contains("null,", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("session.ListenerUrl", source, StringComparison.Ordinal);
        Assert.Contains("NotificationServiceTypes.Request", source, StringComparison.Ordinal);
        Assert.Contains("PublicationNotificationStates.Pending", source, StringComparison.Ordinal);
        Assert.Contains("output inserted.NotificationId, inserted.ProviderRequestSessionId", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cross join dbo.ConsumerRequestTopics topic", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("topic.TopicOrdinal", source, StringComparison.Ordinal);
        Assert.Contains("topic.TopicValue", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestNotificationSource_CapsDeadlineAndAvoidsDuplicates()
    {
        string source = await ReadHostFileAsync("Data", "SqlConsumerRequestRepository.cs");

        Assert.Contains("declare @SqlUtcNow datetime2(7) = sysutcdatetime()", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dateadd(minute, @MaximumDeliveryAgeMinutes, @SqlUtcNow)", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("when @EffectiveExpiryUtc is not null and @EffectiveExpiryUtc < @ConfiguredDeadlineUtc then @EffectiveExpiryUtc", source, StringComparison.Ordinal);
        Assert.Contains("and (@EffectiveExpiryUtc is null or @EffectiveExpiryUtc > @SqlUtcNow)", source, StringComparison.Ordinal);
        Assert.Contains("existing.ProviderRequestSessionId = session.SessionId", source, StringComparison.Ordinal);
        Assert.Contains("existing.NotificationServiceType = @RequestServiceType", source, StringComparison.Ordinal);
        Assert.Contains("with (updlock, holdlock)", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("existingTopic.NotificationId = created.NotificationId", source, StringComparison.Ordinal);
        Assert.Contains("existingTopic.TopicValue = topic.TopicValue", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExpireRequestSource_SuppressesRequestNotificationsInsideExpirationTransaction()
    {
        string source = await ReadHostFileAsync("Data", "SqlConsumerRequestRepository.cs");

        Assert.Contains("await connection.BeginTransactionAsync", source, StringComparison.Ordinal);
        Assert.Contains("set ExpiredUtc = coalesce(ExpiredUtc, @ExpiredUtc)", source, StringComparison.Ordinal);
        Assert.Contains("SqlRequestNotificationSuppression.SuppressForMessageAsync", source, StringComparison.Ordinal);
        Assert.Contains("PublicationNotificationSuppressionReasons.RequestExpired", source, StringComparison.Ordinal);
        Assert.Contains("await transaction.CommitAsync", source, StringComparison.Ordinal);
        Assert.Contains("await RollbackQuietlyAsync(transaction", source, StringComparison.Ordinal);
    }

    private static async Task<string> ReadHostFileAsync(params string[] pathSegments)
    {
        string path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "RapidRedPanda-ISBM-Host",
            Path.Combine(pathSegments)));

        return await File.ReadAllTextAsync(path);
    }
}
