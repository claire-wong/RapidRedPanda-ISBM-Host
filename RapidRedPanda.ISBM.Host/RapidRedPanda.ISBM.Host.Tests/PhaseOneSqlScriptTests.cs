namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class PhaseOneSqlScriptTests
{
    [Fact]
    public async Task FreshInstallScript_CreatesDatabaseAndCurrentSchemaWithoutSqlcmdMode()
    {
        string script = await ReadInstallScriptAsync();

        Assert.Contains("declare @DatabaseName sysname = N'RapidRedPandaISBM';", script, StringComparison.Ordinal);
        Assert.Contains("create database", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("N'use ' + quotename(@DatabaseName)", script, StringComparison.Ordinal);
        Assert.Contains("set xact_abort on", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("begin transaction", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("commit transaction", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rollback transaction", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":setvar", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("$(", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FreshInstallScript_DefinesCoreChannelSecurityAndSchemaVersionObjects()
    {
        string script = await ReadInstallScriptAsync();

        Assert.Contains("create table dbo.SchemaVersions", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("constraint PK_SchemaVersions", script, StringComparison.Ordinal);
        Assert.Contains("constraint UQ_SchemaVersions_VersionNumber", script, StringComparison.Ordinal);
        Assert.Contains("create table dbo.Channels", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ChannelUri nvarchar(512) collate Latin1_General_100_CS_AS_SC not null", script, StringComparison.Ordinal);
        Assert.Contains("AzureTopicName nvarchar(260) not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create unique index UQ_Channels_ChannelUri", script, StringComparison.Ordinal);
        Assert.Contains("create unique index UQ_Channels_AzureTopicName", script, StringComparison.Ordinal);
        Assert.Contains("create table dbo.ChannelSecurityTokens", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UQ_ChannelSecurityTokens_ChannelId_TokenType_NormalizedUsername", script, StringComparison.Ordinal);
        Assert.DoesNotContain("on delete cascade", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FreshInstallScript_DefinesSessionAndBrokerMappingSchema()
    {
        string script = await ReadInstallScriptAsync();

        foreach (string expected in new[]
        {
            "create table dbo.ConsumerSubscriptionSessions",
            "create table dbo.ConsumerSubscriptionSessionTopics",
            "create table dbo.ProviderRequestSessions",
            "create table dbo.ProviderRequestSessionTopics",
            "create table dbo.ProviderPublicationSessions",
            "create table dbo.ConsumerRequestSessions"
        })
        {
            Assert.Contains(expected, script, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("AzureTopicName nvarchar(260) not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AzureSubscriptionName nvarchar(260) not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ListenerUrl nvarchar(2048) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FilterExpressionsJson nvarchar(max) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CK_ConsumerSubscriptionSessions_FilterExpressionsJson", script, StringComparison.Ordinal);
        Assert.Contains("CK_ProviderRequestSessions_FilterExpressionsJson", script, StringComparison.Ordinal);
        Assert.Contains("IX_ConsumerSubscriptionSessions_ChannelId_Open", script, StringComparison.Ordinal);
        Assert.Contains("IX_ConsumerSubscriptionSessions_ChannelId", script, StringComparison.Ordinal);
        Assert.Contains("UQ_ConsumerSubscriptionSessions_AzureSubscription", script, StringComparison.Ordinal);
        Assert.Contains("UQ_ProviderRequestSessions_AzureSubscription", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FreshInstallScript_DefinesDurableRequestResponseAndPublicationContentSchema()
    {
        string script = await ReadInstallScriptAsync();

        foreach (string expected in new[]
        {
            "create table dbo.ConsumerRequests",
            "create table dbo.ConsumerRequestTopics",
            "create table dbo.ConsumerRequestRecipients",
            "create table dbo.ProviderPublications",
            "create table dbo.ProviderPublicationTopics",
            "create table dbo.ProviderPublicationRecipients",
            "create table dbo.RequestResponses",
            "create table dbo.OutstandingRequestResponses"
        })
        {
            Assert.Contains(expected, script, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("MessageContent nvarchar(max) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MessageContent nvarchar(max) not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Payload varbinary(max) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PayloadKind nvarchar(16) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ContentEncoding nvarchar(64) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CK_ConsumerRequests_MessageContentPayloadKind", script, StringComparison.Ordinal);
        Assert.Contains("CK_ProviderPublications_MessageContentPayloadKind", script, StringComparison.Ordinal);
        Assert.Contains("CK_RequestResponses_MessageContentPayloadKind", script, StringComparison.Ordinal);
        Assert.Contains("FK_ConsumerRequestRecipients_ProviderRequestSessions", script, StringComparison.Ordinal);
        Assert.Contains("UQ_ConsumerRequestRecipients_Provider_Outstanding", script, StringComparison.Ordinal);
        Assert.Contains("IX_ProviderPublicationRecipients_Consumer_Outstanding", script, StringComparison.Ordinal);
        Assert.Contains("UQ_RequestResponses_MessageId", script, StringComparison.Ordinal);
        Assert.Contains("UQ_OutstandingRequestResponses_Response", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FreshInstallScript_DefinesNotificationOutboxAndCleanupIndexes()
    {
        string script = await ReadInstallScriptAsync();

        Assert.Contains("create table dbo.Notifications", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create table dbo.NotificationTopics", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NotificationServiceType nvarchar(32) not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RequestMessageId uniqueidentifier null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ConsumerSubscriptionSessionId uniqueidentifier null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProviderRequestSessionId uniqueidentifier null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ConsumerRequestSessionId uniqueidentifier null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DeliveryDeadlineUtc datetime2(7) not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LastAttemptUtc datetime2(7) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LastStatusCode int null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LastError nvarchar(2048) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LockedUntilUtc datetime2(7) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LockOwner nvarchar(128) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ResolutionReason nvarchar(64) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NotificationServiceType in (N''Publication'', N''Request'', N''Response'')", script, StringComparison.Ordinal);
        Assert.Contains("UQ_Notifications_Message_ConsumerSubscriptionSession_Publication", script, StringComparison.Ordinal);
        Assert.Contains("UQ_Notifications_Message_ProviderRequestSession_Request", script, StringComparison.Ordinal);
        Assert.Contains("UQ_Notifications_Message_ConsumerRequestSession_Response", script, StringComparison.Ordinal);
        Assert.Contains("IX_Notifications_Pending", script, StringComparison.Ordinal);
        Assert.Contains("IX_Notifications_ProviderRequestSession_State", script, StringComparison.Ordinal);
        Assert.Contains("IX_Notifications_Response_ConsumerRequestSession_Pending", script, StringComparison.Ordinal);
        Assert.Contains("IX_Notifications_Response_RequestMessage_Pending", script, StringComparison.Ordinal);
        Assert.Contains("IX_Notifications_Request_Message_Pending", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FreshInstallScript_DefinesBinaryTopicSemanticsAndUniqueTopicIndexes()
    {
        string script = await ReadInstallScriptAsync();

        Assert.Equal(5, CountOccurrences(script, "TopicValue nvarchar(512) collate Latin1_General_100_BIN2 not null"));

        foreach (string indexName in new[]
        {
            "UQ_ConsumerSubscriptionSessionTopics_Session_TopicValue",
            "IX_ConsumerSubscriptionSessionTopics_TopicValue",
            "UQ_ProviderRequestSessionTopics_Session_TopicValue",
            "IX_ProviderRequestSessionTopics_TopicValue",
            "UQ_ConsumerRequestTopics_Message_TopicValue",
            "UQ_ProviderPublicationTopics_Message_TopicValue",
            "UQ_NotificationTopics_Notification_TopicValue"
        })
        {
            Assert.Contains(indexName, script, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task FreshInstallScript_InitializesAllCurrentSchemaVersions()
    {
        string script = await ReadInstallScriptAsync();

        foreach (string version in new[]
        {
            "PhaseOne-ChannelPersistence",
            "ChannelAzureTopicBinding-001",
            "ConsumerSubscriptionSessions-001",
            "ConsumerSubscriptionSessionTopics-001",
            "ProviderPublicationSessions-001",
            "ProviderPublicationExpiration-001",
            "ConsumerPublicationListenerUrl-001",
            "RequestServices-001",
            "RequestResponses-001",
            "RequestResponseMessageContentPayload-001",
            "PublicationNotifications-001",
            "PublicationNotificationDispatch-001",
            "PublicationNotifications-002",
            "RequestNotifications-001",
            "SessionNotificationModel-001",
            "ResponseNotifications-001",
            "SessionFilterExpressions-001",
            "TopicValueSemantics-001",
            "RequestRecipientState-001",
            "PublicationMessageContent-001",
            "NotificationCleanupIndexes-001"
        })
        {
            Assert.Contains(version, script, StringComparison.Ordinal);
        }

        Assert.Contains("N''001_CreateDatabase.sql''", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scripts_DoNotCreateFuturePhaseTables()
    {
        string script = await ReadInstallScriptAsync();

        Assert.DoesNotContain("create table dbo.Sessions", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("create table dbo.Publications", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("create table dbo.Topics", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("create table dbo.TransportBindings", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("create table dbo.Outbox", script, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadInstallScriptAsync()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Scripts", "Install", "001_CreateDatabase.sql");
        return await File.ReadAllTextAsync(path);
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int startIndex = 0;

        while (true)
        {
            int index = text.IndexOf(value, startIndex, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return count;
            }

            count++;
            startIndex = index + value.Length;
        }
    }
}
