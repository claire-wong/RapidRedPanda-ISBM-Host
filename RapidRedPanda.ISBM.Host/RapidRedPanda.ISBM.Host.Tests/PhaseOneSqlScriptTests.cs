namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class PhaseOneSqlScriptTests
{
    [Fact]
    public async Task Script_UsesRequiredCaseSensitiveChannelUriCollation()
    {
        string script = await ReadScriptAsync("Install", "002_CreatePhaseOneSchema.sql");

        Assert.Contains("ChannelUri nvarchar(512) collate Latin1_General_100_CS_AS_SC not null", script);
        Assert.Contains("create unique index UQ_Channels_ChannelUri", script);
        Assert.Contains("where DeletedUtc is null", script);
    }

    [Fact]
    public async Task Script_DoesNotCascadeDeleteSecurityTokens()
    {
        string script = await ReadAllScriptsAsync();

        Assert.DoesNotContain("on delete cascade", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Script_DefinesActiveTokenUsernameUniqueness()
    {
        string script = await ReadScriptAsync("Install", "002_CreatePhaseOneSchema.sql");

        Assert.Contains("UQ_ChannelSecurityTokens_ChannelId_TokenType_NormalizedUsername", script);
        Assert.Contains("on dbo.ChannelSecurityTokens (ChannelId, TokenType, NormalizedUsername)", script);
    }

    [Fact]
    public async Task InstallScript_UsesFinalApprovedLengthsAndSchemaVersion()
    {
        string script = await ReadScriptAsync("Install", "002_CreatePhaseOneSchema.sql");

        Assert.Contains("ChannelUri nvarchar(512)", script);
        Assert.Contains("Description nvarchar(2000)", script);
        Assert.Contains("Username nvarchar(256)", script);
        Assert.Contains("PasswordHash nvarchar(512)", script);
        Assert.Contains("create table dbo.SchemaVersions", script);
        Assert.Contains("UQ_SchemaVersions_VersionNumber", script);
        Assert.Contains("PhaseOne-ChannelPersistence", script);
    }

    [Fact]
    public async Task InstallAndMigrationScripts_UseTransactionAndXactAbort()
    {
        string installScript = await ReadScriptAsync("Install", "002_CreatePhaseOneSchema.sql");
        string legacyMigrationScript = await ReadScriptAsync("Migrations", "001_LegacyToPhaseOne.sql");
        string sessionMigrationScript = await ReadScriptAsync("Migrations", "002_AddConsumerSubscriptionSessions.sql");
        string sessionTopicsMigrationScript = await ReadScriptAsync("Migrations", "003_AddConsumerSubscriptionSessionTopics.sql");
        string channelTopicBindingMigrationScript = await ReadScriptAsync("Migrations", "004_AddChannelAzureTopicBinding.sql");
        string providerPublicationSessionMigrationScript = await ReadScriptAsync("Migrations", "005_AddProviderPublicationSessions.sql");
        string listenerUrlMigrationScript = await ReadScriptAsync("Migrations", "009_AddConsumerPublicationListenerUrl.sql");
        string notificationMigrationScript = await ReadScriptAsync("Migrations", "010_AddPublicationNotifications.sql");
        string dispatchMigrationScript = await ReadScriptAsync("Migrations", "011_AddPublicationNotificationDispatch.sql");
        string deliveryDeadlineMigrationScript = await ReadScriptAsync("Migrations", "012_AddPublicationNotificationDeliveryDeadline.sql");
        string requestNotificationMigrationScript = await ReadScriptAsync("Migrations", "013_AddRequestNotifications.sql");

        Assert.Contains("set xact_abort on", installScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("begin transaction", installScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("set xact_abort on", legacyMigrationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("begin transaction", legacyMigrationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("set xact_abort on", sessionMigrationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("begin transaction", sessionMigrationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("set xact_abort on", sessionTopicsMigrationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("begin transaction", sessionTopicsMigrationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("set xact_abort on", channelTopicBindingMigrationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("begin transaction", channelTopicBindingMigrationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("set xact_abort on", providerPublicationSessionMigrationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("begin transaction", providerPublicationSessionMigrationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("set xact_abort on", listenerUrlMigrationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("begin transaction", listenerUrlMigrationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("set xact_abort on", notificationMigrationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("begin transaction", notificationMigrationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("set xact_abort on", dispatchMigrationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("begin transaction", dispatchMigrationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("set xact_abort on", deliveryDeadlineMigrationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("begin transaction", deliveryDeadlineMigrationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("set xact_abort on", requestNotificationMigrationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("begin transaction", requestNotificationMigrationScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MigrationScript_HasLegacyPreflightAndDoesNotDropLegacyTables()
    {
        string script = await ReadScriptAsync("Migrations", "001_LegacyToPhaseOne.sql");

        Assert.Contains("Channel_UUID", script);
        Assert.Contains("Channel_Id", script);
        Assert.Contains("Channel_Type", script);
        Assert.Contains("Channels_LegacyBackup", script);
        Assert.Contains("Latin1_General_100_CS_AS_SC", script);
        Assert.DoesNotContain("drop table dbo.Channels", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("drop table dbo.Channels_LegacyBackup", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConsumerSubscriptionSessionMigration_IsGuardedUpgradeScript()
    {
        string script = await ReadScriptAsync("Migrations", "002_AddConsumerSubscriptionSessions.sql");

        Assert.Contains("object_id(N'dbo.ConsumerSubscriptionSessions', N'U') is null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FK_ConsumerSubscriptionSessions_Channels", script, StringComparison.Ordinal);
        Assert.Contains("PK_ConsumerSubscriptionSessions", script, StringComparison.Ordinal);
        Assert.Contains("CK_ConsumerSubscriptionSessions_SessionType", script, StringComparison.Ordinal);
        Assert.Contains("CK_ConsumerSubscriptionSessions_SessionState", script, StringComparison.Ordinal);
        Assert.Contains("IX_ConsumerSubscriptionSessions_ChannelId_Open", script, StringComparison.Ordinal);
        Assert.Contains("UQ_ConsumerSubscriptionSessions_AzureSubscription", script, StringComparison.Ordinal);
        Assert.Contains("ListenerUrl nvarchar(2048) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("col_length(N'dbo.ConsumerSubscriptionSessions', N'ListenerUrl') is null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'ConsumerSubscriptionSessions-001')", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("drop table dbo.Channels", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("drop table dbo.ChannelSecurityTokens", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SharedAccessKey", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConsumerPublicationListenerUrlMigration_AddsNullableListenerUrlIdempotently()
    {
        string script = await ReadScriptAsync("Migrations", "009_AddConsumerPublicationListenerUrl.sql");

        Assert.Contains("ConsumerPublicationListenerUrl-001", script, StringComparison.Ordinal);
        Assert.Contains("col_length(N'dbo.ConsumerSubscriptionSessions', N'ListenerUrl') is null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("add ListenerUrl nvarchar(2048) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unique index", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("default", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("drop table", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublicationNotificationsMigration_AddsDurablePendingOutboxTables()
    {
        string script = await ReadScriptAsync("Migrations", "010_AddPublicationNotifications.sql");

        Assert.Contains("create table dbo.PublicationNotifications", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PublicationNotificationId uniqueidentifier not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MessageId uniqueidentifier not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ConsumerSessionId uniqueidentifier not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ListenerUrl nvarchar(2048) not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("State nvarchar(32) not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AttemptCount int not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NextAttemptUtc datetime2(7) not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LastAttemptUtc datetime2(7) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LastStatusCode int null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LastError nvarchar(2048) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LockedUntilUtc datetime2(7) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LockOwner nvarchar(128) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ResolutionReason nvarchar(64) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create table dbo.PublicationNotificationTopics", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TopicValue nvarchar(512) not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UQ_PublicationNotifications_Message_ConsumerSession", script, StringComparison.Ordinal);
        Assert.Contains("IX_PublicationNotifications_Pending", script, StringComparison.Ordinal);
        Assert.Contains("FK_PublicationNotifications_Recipients", script, StringComparison.Ordinal);
        Assert.Contains("PublicationNotifications-001", script, StringComparison.Ordinal);
        Assert.DoesNotContain("on delete cascade", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HttpClient", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublicationNotificationDispatchMigration_AddsLeaseRetryAndTerminalStateFields()
    {
        string script = await ReadScriptAsync("Migrations", "011_AddPublicationNotificationDispatch.sql");

        Assert.Contains("PublicationNotificationDispatch-001", script, StringComparison.Ordinal);
        Assert.Contains("LastAttemptUtc datetime2(7) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LastStatusCode int null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LastError nvarchar(2048) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LockedUntilUtc datetime2(7) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LockOwner nvarchar(128) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ResolutionReason nvarchar(64) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("N'Sending'", script, StringComparison.Ordinal);
        Assert.Contains("N'Delivered'", script, StringComparison.Ordinal);
        Assert.Contains("N'RetryScheduled'", script, StringComparison.Ordinal);
        Assert.Contains("N'Failed'", script, StringComparison.Ordinal);
        Assert.Contains("N'Suppressed'", script, StringComparison.Ordinal);
        Assert.Contains("IX_PublicationNotifications_Pending", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DeliveryDeadlineUtc", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("delete from dbo.PublicationNotifications", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublicationNotificationDeliveryDeadlineMigration_AddsDeadlineSafelyAndIdempotently()
    {
        string script = await ReadScriptAsync("Migrations", "012_AddPublicationNotificationDeliveryDeadline.sql");

        Assert.Contains("PublicationNotifications-002", script, StringComparison.Ordinal);
        Assert.Contains("PublicationNotifications-001", script, StringComparison.Ordinal);
        Assert.Contains("PublicationNotificationDispatch-001", script, StringComparison.Ordinal);
        Assert.Contains("col_length(N'dbo.PublicationNotifications', N'DeliveryDeadlineUtc') is null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("add DeliveryDeadlineUtc datetime2(7) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exec sys.sp_executesql", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("set DeliveryDeadlineUtc = dateadd(minute, 1440, CreatedUtc)", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("where DeliveryDeadlineUtc is null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("alter column DeliveryDeadlineUtc datetime2(7) not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("N'Expired'", script, StringComparison.Ordinal);
        Assert.Contains("IX_PublicationNotifications_Pending", script, StringComparison.Ordinal);
        Assert.Contains("State, NextAttemptUtc, LockedUntilUtc, DeliveryDeadlineUtc, CreatedUtc, PublicationNotificationId", script, StringComparison.Ordinal);
        Assert.DoesNotContain("delete from dbo.PublicationNotifications", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("delete from dbo.PublicationNotificationTopics", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("set AttemptCount", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("set State", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("set NextAttemptUtc", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequestNotificationsMigration_GeneralizesSharedNotificationSchema()
    {
        string script = await ReadScriptAsync("Migrations", "013_AddRequestNotifications.sql");

        Assert.Contains("RequestNotifications-001", script, StringComparison.Ordinal);
        Assert.Contains("RequestServices-001", script, StringComparison.Ordinal);
        Assert.Contains("PublicationNotifications-002", script, StringComparison.Ordinal);
        Assert.Contains("NotificationServiceType nvarchar(32)", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("set NotificationServiceType = N''Publication''", script, StringComparison.Ordinal);
        Assert.Contains("alter column NotificationServiceType nvarchar(32) not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProviderRequestSessionId uniqueidentifier null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("alter column ConsumerSessionId uniqueidentifier null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FK_PublicationNotifications_Recipients", script, StringComparison.Ordinal);
        Assert.Contains("FK_PublicationNotifications_ProviderRequestSessions", script, StringComparison.Ordinal);
        Assert.Contains("references dbo.ConsumerSubscriptionSessions (SessionId)", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CK_PublicationNotifications_ServiceType", script, StringComparison.Ordinal);
        Assert.Contains("CK_PublicationNotifications_RecipientShape", script, StringComparison.Ordinal);
        Assert.Contains("exec sys.sp_executesql", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NotificationServiceType in (N''Publication'', N''Request'')", script, StringComparison.Ordinal);
        Assert.Contains("UQ_PublicationNotifications_Message_ConsumerSession", script, StringComparison.Ordinal);
        Assert.Contains("where NotificationServiceType = N''Publication''", script, StringComparison.Ordinal);
        Assert.Contains("UQ_PublicationNotifications_Message_ProviderRequestSession_Request", script, StringComparison.Ordinal);
        Assert.Contains("where NotificationServiceType = N''Request''", script, StringComparison.Ordinal);
        Assert.Contains("IX_PublicationNotifications_Pending", script, StringComparison.Ordinal);
        Assert.Contains("NotificationServiceType, State, NextAttemptUtc, LockedUntilUtc, DeliveryDeadlineUtc, CreatedUtc, PublicationNotificationId", script, StringComparison.Ordinal);
        Assert.Contains("IX_PublicationNotifications_RequestSession_State", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PostRequestAsync", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("delete from dbo.PublicationNotifications", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FreshInstall_IncludesCurrentPublicationNotificationSchema()
    {
        string script = await ReadScriptAsync("Install", "002_CreatePhaseOneSchema.sql");

        Assert.Contains("create table dbo.ProviderRequestSessions", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create table dbo.ProviderRequestSessionTopics", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create table dbo.ConsumerRequestSessions", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ListenerUrl nvarchar(2048) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create table dbo.ConsumerRequests", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create table dbo.RequestResponses", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create table dbo.OutstandingRequestResponses", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create table dbo.Notifications", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create table dbo.NotificationTopics", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NotificationServiceType nvarchar(32) not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ConsumerSubscriptionSessionId uniqueidentifier null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProviderRequestSessionId uniqueidentifier null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ConsumerRequestSessionId uniqueidentifier null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RequestMessageId uniqueidentifier null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DeliveryDeadlineUtc datetime2(7) not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LastAttemptUtc datetime2(7) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LastStatusCode int null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LastError nvarchar(2048) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LockedUntilUtc datetime2(7) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LockOwner nvarchar(128) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ResolutionReason nvarchar(64) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("N'Expired'", script, StringComparison.Ordinal);
        Assert.Contains("FK_Notifications_ConsumerSubscriptionSessions", script, StringComparison.Ordinal);
        Assert.Contains("FK_Notifications_PublicationRecipients", script, StringComparison.Ordinal);
        Assert.Contains("foreign key (MessageId, ConsumerSubscriptionSessionId)", script, StringComparison.Ordinal);
        Assert.Contains("references dbo.ProviderPublicationRecipients (MessageId, ConsumerSessionId)", script, StringComparison.Ordinal);
        Assert.Contains("FK_Notifications_ProviderRequestSessions", script, StringComparison.Ordinal);
        Assert.Contains("FK_Notifications_ConsumerRequestSessions", script, StringComparison.Ordinal);
        Assert.Contains("FK_Notifications_RequestMessage_ConsumerRequests", script, StringComparison.Ordinal);
        Assert.Contains("references dbo.ProviderRequestSessions (SessionId)", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CK_Notifications_ServiceType", script, StringComparison.Ordinal);
        Assert.Contains("CK_Notifications_RecipientShape", script, StringComparison.Ordinal);
        Assert.Contains("NotificationServiceType in (N'Publication', N'Request', N'Response')", script, StringComparison.Ordinal);
        Assert.Contains("NotificationServiceType = N'Response'", script, StringComparison.Ordinal);
        Assert.Contains("and RequestMessageId is null", script, StringComparison.Ordinal);
        Assert.Contains("and RequestMessageId is not null", script, StringComparison.Ordinal);
        Assert.Contains("FK_NotificationTopics_Notifications", script, StringComparison.Ordinal);
        Assert.Contains("UQ_Notifications_Message_ConsumerSubscriptionSession_Publication", script, StringComparison.Ordinal);
        Assert.Contains("where NotificationServiceType = N'Publication'", script, StringComparison.Ordinal);
        Assert.Contains("UQ_Notifications_Message_ProviderRequestSession_Request", script, StringComparison.Ordinal);
        Assert.Contains("where NotificationServiceType = N'Request'", script, StringComparison.Ordinal);
        Assert.Contains("UQ_Notifications_Message_ConsumerRequestSession_Response", script, StringComparison.Ordinal);
        Assert.Contains("on dbo.Notifications (MessageId, ConsumerRequestSessionId)", script, StringComparison.Ordinal);
        Assert.Contains("where NotificationServiceType = N'Response'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("on dbo.Notifications (RequestMessageId, ConsumerRequestSessionId)", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IX_Notifications_Pending", script, StringComparison.Ordinal);
        Assert.Contains("NotificationServiceType, State, NextAttemptUtc, LockedUntilUtc, DeliveryDeadlineUtc, CreatedUtc, NotificationId", script, StringComparison.Ordinal);
        Assert.Contains("include (MessageId, RequestMessageId, ConsumerSubscriptionSessionId, ProviderRequestSessionId, ConsumerRequestSessionId, ListenerUrl, AttemptCount, LockOwner)", script, StringComparison.Ordinal);
        Assert.Contains("IX_Notifications_ProviderRequestSession_State", script, StringComparison.Ordinal);
        Assert.Contains("PublicationNotifications-002", script, StringComparison.Ordinal);
        Assert.Contains("RequestServices-001", script, StringComparison.Ordinal);
        Assert.Contains("RequestResponses-001", script, StringComparison.Ordinal);
        Assert.Contains("RequestNotifications-001", script, StringComparison.Ordinal);
        Assert.Contains("SessionNotificationModel-001", script, StringComparison.Ordinal);
        Assert.Contains("ResponseNotifications-001", script, StringComparison.Ordinal);
        Assert.Contains("check (SessionType in (N'ConsumerPublicationSubscription'))", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResponseNotificationsMigration_AddsRequestCorrelationAndResponseShapeOnly()
    {
        string script = await ReadScriptAsync("Migrations", "015_AddResponseNotificationSchema.sql");

        Assert.Contains("ResponseNotifications-001", script, StringComparison.Ordinal);
        Assert.Contains("SessionNotificationModel-001", script, StringComparison.Ordinal);
        Assert.Contains("col_length(N'dbo.Notifications', N'RequestMessageId') is null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("add RequestMessageId uniqueidentifier null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exec sys.sp_executesql", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NotificationServiceType in (N''Publication'', N''Request'', N''Response'')", script, StringComparison.Ordinal);
        Assert.Contains("NotificationServiceType = N''Publication''", script, StringComparison.Ordinal);
        Assert.Contains("NotificationServiceType = N''Request''", script, StringComparison.Ordinal);
        Assert.Contains("NotificationServiceType = N''Response''", script, StringComparison.Ordinal);
        Assert.Contains("and RequestMessageId is null", script, StringComparison.Ordinal);
        Assert.Contains("and RequestMessageId is not null", script, StringComparison.Ordinal);
        Assert.Contains("and ConsumerRequestSessionId is not null", script, StringComparison.Ordinal);
        Assert.Contains("and ConsumerSubscriptionSessionId is null", script, StringComparison.Ordinal);
        Assert.Contains("and ProviderRequestSessionId is null", script, StringComparison.Ordinal);
        Assert.Contains("UQ_Notifications_Message_ConsumerRequestSession_Response", script, StringComparison.Ordinal);
        Assert.Contains("on dbo.Notifications (MessageId, ConsumerRequestSessionId)", script, StringComparison.Ordinal);
        Assert.Contains("where NotificationServiceType = N''Response''", script, StringComparison.Ordinal);
        Assert.Contains("FK_Notifications_RequestMessage_ConsumerRequests", script, StringComparison.Ordinal);
        Assert.Contains("references dbo.ConsumerRequests (MessageId)", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("insert dbo.SchemaVersions", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ResponseMessageId", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NotificationTopics", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("on dbo.Notifications (RequestMessageId, ConsumerRequestSessionId)", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PostResponse", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReadResponse", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResponseNotificationSchema_InstallMigrationAndValidationStayInParity()
    {
        string install = await ReadScriptAsync("Install", "002_CreatePhaseOneSchema.sql");
        string migration = await ReadScriptAsync("Migrations", "015_AddResponseNotificationSchema.sql");
        string validation = await ReadScriptAsync("Validation", "ValidatePhaseOneSchema.sql");

        foreach (string script in new[] { install, migration, validation })
        {
            Assert.Contains("RequestMessageId", script, StringComparison.Ordinal);
            Assert.Contains("ConsumerRequestSessionId", script, StringComparison.Ordinal);
            Assert.Contains("Response", script, StringComparison.Ordinal);
            Assert.Contains("UQ_Notifications_Message_ConsumerRequestSession_Response", script, StringComparison.Ordinal);
            Assert.Contains("FK_Notifications_RequestMessage_ConsumerRequests", script, StringComparison.Ordinal);
        }

        Assert.Contains("NotificationServiceType in (N'Publication', N'Request', N'Response')", install, StringComparison.Ordinal);
        Assert.Contains("NotificationServiceType in (N''Publication'', N''Request'', N''Response'')", migration, StringComparison.Ordinal);
        Assert.Contains("NotificationServiceType = N'Response'", install, StringComparison.Ordinal);
        Assert.Contains("NotificationServiceType = N''Response''", migration, StringComparison.Ordinal);
        Assert.Contains("ConsumerSubscriptionSessionId is null", install, StringComparison.Ordinal);
        Assert.Contains("ProviderRequestSessionId is null", install, StringComparison.Ordinal);
        Assert.Contains("RequestMessageId is not null", install, StringComparison.Ordinal);
        Assert.Contains("messageColumnDefinition.name = N'MessageId'", validation, StringComparison.Ordinal);
        Assert.Contains("sessionColumnDefinition.name = N'ConsumerRequestSessionId'", validation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LateNotificationMigrations_AvoidSameBatchAddedColumnReferences()
    {
        string requestMigration = await ReadScriptAsync("Migrations", "013_AddRequestNotifications.sql");
        string sessionMigration = await ReadScriptAsync("Migrations", "014_RefactorSessionNotificationModel.sql");
        string responseMigration = await ReadScriptAsync("Migrations", "015_AddResponseNotificationSchema.sql");

        Assert.Contains("begin try", requestMigration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("begin transaction", requestMigration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exec sys.sp_executesql", requestMigration, StringComparison.OrdinalIgnoreCase);
        Assert.True(HasOnlyFinalGoTerminator(requestMigration));

        Assert.Contains("begin try", sessionMigration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("begin transaction", sessionMigration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exec sys.sp_executesql", sessionMigration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exec sys.sp_rename", sessionMigration, StringComparison.OrdinalIgnoreCase);
        Assert.True(HasOnlyFinalGoTerminator(sessionMigration));

        Assert.Contains("begin try", responseMigration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("begin transaction", responseMigration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exec sys.sp_executesql", responseMigration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("col_length(N'dbo.Notifications', N'RequestMessageId') is null", responseMigration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("insert dbo.SchemaVersions", responseMigration, StringComparison.OrdinalIgnoreCase);
        Assert.True(HasOnlyFinalGoTerminator(responseMigration));
    }

    [Fact]
    public async Task SessionNotificationModelMigration_SeparatesProviderRequestSessionsAndGeneralizesNotifications()
    {
        string script = await ReadScriptAsync("Migrations", "014_RefactorSessionNotificationModel.sql");

        Assert.Contains("SessionNotificationModel-001", script, StringComparison.Ordinal);
        Assert.Contains("RequestNotifications-001", script, StringComparison.Ordinal);
        Assert.Contains("create table dbo.ProviderRequestSessions", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create table dbo.ProviderRequestSessionTopics", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("insert dbo.ProviderRequestSessions", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("insert dbo.ProviderRequestSessionTopics", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SessionType = N''ProviderRequestSubscription''", script, StringComparison.Ordinal);
        Assert.Contains("exec sys.sp_rename N'dbo.PublicationNotifications', N'Notifications'", script, StringComparison.Ordinal);
        Assert.Contains("exec sys.sp_rename N'dbo.PublicationNotificationTopics', N'NotificationTopics'", script, StringComparison.Ordinal);
        Assert.Contains("exec sys.sp_rename N'dbo.Notifications.PublicationNotificationId', N'NotificationId', N'COLUMN'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("exec sys.sp_rename N'dbo.Notifications.ConsumerSessionId'", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("add ConsumerSubscriptionSessionId uniqueidentifier null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("set ConsumerSubscriptionSessionId = ConsumerSessionId", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("drop column ConsumerSessionId", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("add ConsumerRequestSessionId uniqueidentifier null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("alter table dbo.ConsumerRequestSessions", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("add ListenerUrl nvarchar(2048) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FK_Notifications_ConsumerSubscriptionSessions", script, StringComparison.Ordinal);
        Assert.Contains("FK_Notifications_PublicationRecipients", script, StringComparison.Ordinal);
        Assert.Contains("foreign key (MessageId, ConsumerSubscriptionSessionId)", script, StringComparison.Ordinal);
        Assert.Contains("references dbo.ProviderPublicationRecipients (MessageId, ConsumerSessionId)", script, StringComparison.Ordinal);
        Assert.Contains("FK_Notifications_ProviderRequestSessions", script, StringComparison.Ordinal);
        Assert.Contains("FK_Notifications_ConsumerRequestSessions", script, StringComparison.Ordinal);
        Assert.Contains("alter table dbo.RequestResponses drop constraint FK_RequestResponses_ProviderRequestSessions", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("references dbo.ProviderRequestSessions (SessionId)", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CK_Notifications_ServiceType", script, StringComparison.Ordinal);
        Assert.Contains("CK_Notifications_RecipientShape", script, StringComparison.Ordinal);
        Assert.Contains("UQ_Notifications_Message_ConsumerSubscriptionSession_Publication", script, StringComparison.Ordinal);
        Assert.Contains("UQ_Notifications_Message_ProviderRequestSession_Request", script, StringComparison.Ordinal);
        Assert.Contains("delete topic", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delete from dbo.ConsumerSubscriptionSessions", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("check (SessionType in (N'ConsumerPublicationSubscription'))", script, StringComparison.Ordinal);
        Assert.Contains("exec sys.sp_executesql", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("N'Response'", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConsumerSubscriptionSessionTopicsMigration_IsGuardedUpgradeScript()
    {
        string script = await ReadScriptAsync("Migrations", "003_AddConsumerSubscriptionSessionTopics.sql");

        Assert.Contains("object_id(N'dbo.ConsumerSubscriptionSessionTopics', N'U') is null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FK_ConsumerSubscriptionSessionTopics_Sessions", script, StringComparison.Ordinal);
        Assert.Contains("PK_ConsumerSubscriptionSessionTopics", script, StringComparison.Ordinal);
        Assert.Contains("UQ_ConsumerSubscriptionSessionTopics_Session_Ordinal", script, StringComparison.Ordinal);
        Assert.Contains("UQ_ConsumerSubscriptionSessionTopics_Session_TopicValue", script, StringComparison.Ordinal);
        Assert.Contains("ConsumerSubscriptionSessionTopics-001", script, StringComparison.Ordinal);
        Assert.DoesNotContain("drop table dbo.Channels", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("drop table dbo.ChannelSecurityTokens", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SharedAccessKey", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChannelAzureTopicBindingMigration_BackfillsAndGuardsStoredTopicNames()
    {
        string script = await ReadScriptAsync("Migrations", "004_AddChannelAzureTopicBinding.sql");

        Assert.Contains("AzureTopicName nvarchar(260)", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("N''isbm-ch-'' + replace(convert(nvarchar(36), ChannelId), N''-'', N'''')", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("alter column AzureTopicName nvarchar(260) not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UQ_Channels_AzureTopicName", script, StringComparison.Ordinal);
        Assert.Contains("ChannelAzureTopicBinding-001", script, StringComparison.Ordinal);
        Assert.True(CountOccurrences(script, "exec sys.sp_executesql") >= 4);
        Assert.DoesNotContain("drop table dbo.Channels", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("drop table dbo.ChannelSecurityTokens", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SharedAccessKey", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProviderPublicationSessionMigration_IsGuardedSqlOnlyUpgradeScript()
    {
        string script = await ReadScriptAsync("Migrations", "005_AddProviderPublicationSessions.sql");

        Assert.Contains("object_id(N'dbo.ProviderPublicationSessions', N'U') is null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SessionId uniqueidentifier not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ChannelId uniqueidentifier not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CreatedUtc datetime2(7) not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("default sysutcdatetime() for CreatedUtc", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PK_ProviderPublicationSessions", script, StringComparison.Ordinal);
        Assert.Contains("FK_ProviderPublicationSessions_Channels", script, StringComparison.Ordinal);
        Assert.Contains("references dbo.Channels (ChannelId)", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CK_ProviderPublicationSessions_SessionState", script, StringComparison.Ordinal);
        Assert.Contains("IX_ProviderPublicationSessions_ChannelId_Open", script, StringComparison.Ordinal);
        Assert.Contains("ProviderPublicationSessions-001", script, StringComparison.Ordinal);
        Assert.DoesNotContain("AzureTopicName", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AzureSubscriptionName", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SharedAccessKey", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("drop table dbo.Channels", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("drop table dbo.ChannelSecurityTokens", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidationScript_IsReadOnlyAndChecksExpectedObjects()
    {
        string script = await ReadScriptAsync("Validation", "ValidatePhaseOneSchema.sql");

        Assert.Contains("dbo.Channels", script);
        Assert.Contains("dbo.ChannelSecurityTokens", script);
        Assert.Contains("dbo.SchemaVersions", script);
        Assert.Contains("dbo.ConsumerSubscriptionSessions", script);
        Assert.Contains("dbo.ProviderRequestSessions", script);
        Assert.Contains("dbo.ProviderRequestSessionTopics", script);
        Assert.Contains("dbo.ConsumerRequests", script);
        Assert.Contains("dbo.ConsumerRequestTopics", script);
        Assert.Contains("dbo.RequestResponses", script);
        Assert.Contains("dbo.OutstandingRequestResponses", script);
        Assert.Contains("dbo.Notifications", script);
        Assert.Contains("dbo.NotificationTopics", script);
        Assert.Contains("dbo.ConsumerRequestSessions", script);
        Assert.Contains("RequestServices-001", script);
        Assert.Contains("RequestResponses-001", script);
        Assert.Contains("RequestNotifications-001", script);
        Assert.Contains("SessionNotificationModel-001", script);
        Assert.Contains("ResponseNotifications-001", script);
        Assert.Contains("dbo.PublicationNotifications", script);
        Assert.Contains("dbo.PublicationNotificationTopics", script);
        Assert.Contains("ListenerUrl", script);
        Assert.Contains("max_length = 4096", script);
        Assert.Contains("is_nullable = 1", script);
        Assert.Contains("UQ_Notifications_Message_ConsumerSubscriptionSession_Publication", script);
        Assert.Contains("IX_Notifications_Pending", script);
        Assert.Contains("LastAttemptUtc", script);
        Assert.Contains("LastStatusCode", script);
        Assert.Contains("LastError", script);
        Assert.Contains("LockedUntilUtc", script);
        Assert.Contains("LockOwner", script);
        Assert.Contains("ResolutionReason", script);
        Assert.Contains("DeliveryDeadlineUtc", script);
        Assert.Contains("NotificationServiceType", script);
        Assert.Contains("ProviderRequestSessionId", script);
        Assert.Contains("ConsumerRequestSessionId", script);
        Assert.Contains("RequestMessageId", script);
        Assert.Contains("Response", script);
        Assert.Contains("CK_Notifications_ServiceType", script);
        Assert.Contains("CK_Notifications_RecipientShape", script);
        Assert.Contains("FK_Notifications_ProviderRequestSessions", script);
        Assert.Contains("FK_Notifications_ConsumerRequestSessions", script);
        Assert.Contains("FK_Notifications_RequestMessage_ConsumerRequests", script);
        Assert.Contains("UQ_Notifications_Message_ConsumerRequestSession_Response", script);
        Assert.Contains("messageColumnDefinition.name = N'MessageId'", script);
        Assert.Contains("sessionColumnDefinition.name = N'ConsumerRequestSessionId'", script);
        Assert.Contains("FK_Notifications_PublicationRecipients", script);
        Assert.Contains("parentColumn.name = N'ConsumerSubscriptionSessionId'", script);
        Assert.Contains("referencedColumn.name = N'ConsumerSessionId'", script);
        Assert.Contains("FK_RequestResponses_ProviderRequestSessions", script);
        Assert.Contains("referenced_object_id = object_id(N'dbo.ProviderRequestSessions')", script);
        Assert.Contains("UQ_Notifications_Message_ProviderRequestSession_Request", script);
        Assert.Contains("IX_Notifications_ProviderRequestSession_State", script);
        Assert.Contains("Expired", script);
        Assert.Contains("Latin1_General_100_CS_AS_SC", script);
        Assert.Contains("/Plant/A", script);
        Assert.DoesNotContain("create table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alter table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("insert dbo.", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("update dbo.", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("delete from", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("drop table", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TopicValueHardeningMigration_AlignsEqualityAndAddsRequestTopicUniqueness()
    {
        string migration = await ReadScriptAsync("Migrations", "018_HardenTopicValueSemantics.sql");
        string install = await ReadScriptAsync("Install", "002_CreatePhaseOneSchema.sql");
        string validation = await ReadScriptAsync("Validation", "ValidatePhaseOneSchema.sql");

        Assert.Contains("set xact_abort on", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("begin transaction", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SessionFilterExpressions-001", migration, StringComparison.Ordinal);
        Assert.Contains("TopicValueSemantics-001", migration, StringComparison.Ordinal);
        Assert.Contains("Latin1_General_100_BIN2", migration, StringComparison.Ordinal);
        Assert.Contains("having count(*) > 1", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UQ_ConsumerRequestTopics_Message_TopicValue", migration, StringComparison.Ordinal);
        Assert.Contains("rollback transaction", migration, StringComparison.OrdinalIgnoreCase);

        foreach (string tableName in new[]
        {
            "ConsumerSubscriptionSessionTopics",
            "ProviderRequestSessionTopics",
            "ConsumerRequestTopics",
            "ProviderPublicationTopics",
            "NotificationTopics"
        })
        {
            Assert.Contains(tableName, migration, StringComparison.Ordinal);
            Assert.Contains(tableName, validation, StringComparison.Ordinal);
        }

        Assert.Equal(5, CountOccurrences(install, "TopicValue nvarchar(512) collate Latin1_General_100_BIN2 not null"));
        Assert.Contains("UQ_ConsumerRequestTopics_Message_TopicValue", install, StringComparison.Ordinal);
        Assert.Contains("TopicValueSemantics-001", install, StringComparison.Ordinal);
        Assert.Contains("Latin1_General_100_BIN2", validation, StringComparison.Ordinal);
        Assert.Contains("UQ_ConsumerRequestTopics_Message_TopicValue", validation, StringComparison.Ordinal);
        Assert.DoesNotContain("create table dbo.Topics", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("on delete cascade", migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Validation_ProvesTopicOwnershipForeignKeyShapeAndSafety()
    {
        string validation = await ReadScriptAsync("Validation", "ValidatePhaseOneSchema.sql");

        foreach (string foreignKeyName in new[]
        {
            "FK_ConsumerSubscriptionSessionTopics_Sessions",
            "FK_ProviderRequestSessionTopics_Sessions",
            "FK_ConsumerRequestTopics_Requests",
            "FK_ProviderPublicationTopics_Publications",
            "FK_NotificationTopics_Notifications"
        })
        {
            Assert.Contains(foreignKeyName, validation, StringComparison.Ordinal);
        }

        Assert.Contains("sys.foreign_key_columns", validation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("parent_object_id = object_id(expected.ChildTable)", validation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("referenced_object_id = object_id(expected.ParentTable)", validation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delete_referential_action = 0", validation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_disabled = 0", validation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_not_trusted = 0", validation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("childColumnDefinition.name = expected.ChildColumn", validation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("parentColumnDefinition.name = expected.ParentColumn", validation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Validation_ProvesEveryTopicValueIndexDefinition()
    {
        string validation = await ReadScriptAsync("Validation", "ValidatePhaseOneSchema.sql");

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
            Assert.Contains(indexName, validation, StringComparison.Ordinal);
        }

        Assert.Contains("indexDefinition.is_unique = expected.IsUnique", validation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("firstKey.key_ordinal = 1", validation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secondKey.key_ordinal = 2", validation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("includedColumn.is_included_column = 1", validation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expected.KeyColumnCount", validation, StringComparison.Ordinal);
        Assert.Contains("expected.IncludedColumnCount", validation, StringComparison.Ordinal);
        Assert.Contains("indexDefinition.is_disabled = 0", validation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("indexDefinition.has_filter = 0", validation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scripts_DoNotCreateFuturePhaseTables()
    {
        string script = await ReadAllScriptsAsync();

        Assert.DoesNotContain("create table dbo.Sessions", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("create table dbo.Publications", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("create table dbo.Topics", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("create table dbo.TransportBindings", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("create table dbo.Outbox", script, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadScriptAsync(params string[] pathSegments)
    {
        string path = Path.Combine([AppContext.BaseDirectory, "Scripts", .. pathSegments]);
        return await File.ReadAllTextAsync(path);
    }

    private static async Task<string> ReadAllScriptsAsync()
    {
        string scriptsRoot = Path.Combine(AppContext.BaseDirectory, "Scripts");
        string[] scripts = Directory.GetFiles(scriptsRoot, "*.sql", SearchOption.AllDirectories);
        string[] contents = await Task.WhenAll(scripts.Select(path => File.ReadAllTextAsync(path)));
        return string.Join(Environment.NewLine, contents);
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

    private static bool HasOnlyFinalGoTerminator(string script)
    {
        string normalized = script.Replace("\r\n", "\n", StringComparison.Ordinal);
        string trimmed = normalized.TrimEnd();
        int finalGoIndex = trimmed.LastIndexOf("\ngo", StringComparison.OrdinalIgnoreCase);
        if (finalGoIndex < 0)
        {
            return CountOccurrences(trimmed, "\ngo") == 0;
        }

        return finalGoIndex >= 0 &&
            finalGoIndex == trimmed.Length - "\ngo".Length &&
            CountOccurrences(trimmed[..finalGoIndex], "\ngo") == 0;
    }
}
