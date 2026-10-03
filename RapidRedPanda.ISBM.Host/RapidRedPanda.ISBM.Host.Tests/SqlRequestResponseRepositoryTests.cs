namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class SqlRequestResponseRepositoryTests
{
    [Fact]
    public async Task InsertResponseSource_CreatesResponseNotificationInsideResponseTransaction()
    {
        string source = await ReadHostFileAsync("Data", "SqlRequestResponseRepository.cs");

        Assert.Contains("await connection.BeginTransactionAsync", source, StringComparison.Ordinal);
        Assert.Contains("insert dbo.RequestResponses", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("await InsertResponseNotificationAsync(", source, StringComparison.Ordinal);
        Assert.Contains("await transaction.CommitAsync", source, StringComparison.Ordinal);
        Assert.Contains("await RollbackQuietlyAsync(transaction", source, StringComparison.Ordinal);
        Assert.Contains("RequestResponseRepositoryFailureKind.DatabaseFailure", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResponseNotificationSource_UsesContractShapeWithoutTopics()
    {
        string source = await ReadHostFileAsync("Data", "SqlRequestResponseRepository.cs");

        Assert.Contains("@ResponseServiceType", source, StringComparison.Ordinal);
        Assert.Contains("NotificationServiceTypes.Response", source, StringComparison.Ordinal);
        Assert.Contains("MessageId,", source, StringComparison.Ordinal);
        Assert.Contains("RequestMessageId,", source, StringComparison.Ordinal);
        Assert.Contains("ConsumerSubscriptionSessionId,", source, StringComparison.Ordinal);
        Assert.Contains("ProviderRequestSessionId,", source, StringComparison.Ordinal);
        Assert.Contains("ConsumerRequestSessionId,", source, StringComparison.Ordinal);
        Assert.Contains("@MessageId", source, StringComparison.Ordinal);
        Assert.Contains("@RequestMessageId", source, StringComparison.Ordinal);
        Assert.Contains("null,\r\n                null,\r\n                session.SessionId", source.ReplaceLineEndings("\r\n"), StringComparison.Ordinal);
        Assert.Contains("session.ListenerUrl", source, StringComparison.Ordinal);
        Assert.Contains("@PendingState", source, StringComparison.Ordinal);
        Assert.Contains("PublicationNotificationStates.Pending", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NotificationTopics", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConsumerRequestTopics", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderPublicationTopics", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResponseNotificationSource_UsesEligibleConsumerRequestSessionAndListenerSnapshot()
    {
        string source = await ReadHostFileAsync("Data", "SqlRequestResponseRepository.cs");

        Assert.Contains("from dbo.ConsumerRequestSessions session", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("session.SessionId = @ConsumerRequestSessionId", source, StringComparison.Ordinal);
        Assert.Contains("session.ListenerUrl is not null", source, StringComparison.Ordinal);
        Assert.Contains("ltrim(rtrim(session.ListenerUrl)) <> N''", source, StringComparison.Ordinal);
        Assert.DoesNotContain("request.EffectiveExpiryUtc < @ConfiguredDeadlineUtc", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConsumerRequestTopics", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResponseNotificationSource_CapsDeadlineAndAvoidsDuplicateLogicalRows()
    {
        string source = await ReadHostFileAsync("Data", "SqlRequestResponseRepository.cs");

        Assert.Contains("declare @SqlUtcNow datetime2(7) = sysutcdatetime()", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dateadd(minute, @MaximumDeliveryAgeMinutes, @SqlUtcNow)", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("_notificationOptions.MaximumDeliveryAgeMinutes", source, StringComparison.Ordinal);
        Assert.Contains("existing.MessageId = @MessageId", source, StringComparison.Ordinal);
        Assert.Contains("existing.ConsumerRequestSessionId = @ConsumerRequestSessionId", source, StringComparison.Ordinal);
        Assert.Contains("existing.NotificationServiceType = @ResponseServiceType", source, StringComparison.Ordinal);
        Assert.DoesNotContain("existing.RequestMessageId = @RequestMessageId", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClaimDueSource_NowIncludesResponseNotifications()
    {
        string source = await ReadHostFileAsync("Data", "SqlPublicationNotificationRepository.cs");

        Assert.Contains("notification.NotificationServiceType in (@PublicationServiceType, @RequestServiceType, @ResponseServiceType)", source, StringComparison.Ordinal);
        Assert.Contains("NotificationServiceTypes.Response =>", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveResponseSource_SuppressesOnlyRemovedOutstandingResponseNotification()
    {
        string source = await ReadHostFileAsync("Data", "SqlRequestResponseRepository.cs");

        Assert.Contains("declare @RemovedResponses table", source, StringComparison.Ordinal);
        Assert.Contains("output inserted.MessageId", source, StringComparison.Ordinal);
        Assert.Contains("into @RemovedResponses (MessageId)", source, StringComparison.Ordinal);
        Assert.Contains("delete dbo.OutstandingRequestResponses", source, StringComparison.Ordinal);
        Assert.Contains("inner join @RemovedResponses removed", source, StringComparison.Ordinal);
        Assert.Contains("notification.NotificationServiceType = @ResponseServiceType", source, StringComparison.Ordinal);
        Assert.Contains("notification.ConsumerRequestSessionId = @ConsumerRequestSessionId", source, StringComparison.Ordinal);
        Assert.Contains("notification.State in (@PendingState, @RetryScheduledState)", source, StringComparison.Ordinal);
        Assert.Contains("PublicationNotificationSuppressionReasons.ResponseRemoved", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlResponseNotificationSuppression.SuppressForRequestAsync(\r\n                        connection,\r\n                        transaction,\r\n                        requestMessageId", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadResponseSource_DoesNotSuppressResponseNotifications()
    {
        string source = await ReadHostFileAsync("Data", "SqlRequestResponseRepository.cs");
        int readStart = source.IndexOf("public async Task<ReadRequestResponseResult> ReadResponseAsync", StringComparison.Ordinal);
        int removeStart = source.IndexOf("public async Task<RequestResponseRepositoryResult> RemoveOutstandingResponseAsync", StringComparison.Ordinal);
        string readSource = source[readStart..removeStart];

        Assert.Contains("insert dbo.OutstandingRequestResponses", readSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlResponseNotificationSuppression", readSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PublicationNotificationStates.Suppressed", readSource, StringComparison.Ordinal);
        Assert.DoesNotContain("NotificationServiceTypes.Response", readSource, StringComparison.Ordinal);
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
