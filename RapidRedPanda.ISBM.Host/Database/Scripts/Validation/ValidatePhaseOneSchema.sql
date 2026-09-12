/*
    RapidRedPanda ISBM Host
    Read-only phase-one schema validation.

    This script does not create, alter, or delete schema/data. It throws when
    validation fails.
*/

set nocount on;
set transaction isolation level read committed;

if object_id(N'dbo.Channels', N'U') is null
    throw 53000, 'Validation failed: dbo.Channels does not exist.', 1;

if object_id(N'dbo.ChannelSecurityTokens', N'U') is null
    throw 53001, 'Validation failed: dbo.ChannelSecurityTokens does not exist.', 1;

if object_id(N'dbo.SchemaVersions', N'U') is null
    throw 53002, 'Validation failed: dbo.SchemaVersions does not exist.', 1;

if object_id(N'dbo.ConsumerSubscriptionSessions', N'U') is null
    throw 53026, 'Validation failed: dbo.ConsumerSubscriptionSessions does not exist.', 1;

if object_id(N'dbo.ProviderRequestSessions', N'U') is null
    throw 53051, 'Validation failed: dbo.ProviderRequestSessions does not exist.', 1;

if object_id(N'dbo.ProviderRequestSessionTopics', N'U') is null
    throw 53052, 'Validation failed: dbo.ProviderRequestSessionTopics does not exist.', 1;

if object_id(N'dbo.ConsumerRequestSessions', N'U') is null
    throw 53057, 'Validation failed: dbo.ConsumerRequestSessions does not exist.', 1;

if object_id(N'dbo.ConsumerRequests', N'U') is null
    throw 53058, 'Validation failed: dbo.ConsumerRequests does not exist.', 1;

if object_id(N'dbo.ConsumerRequestTopics', N'U') is null
    throw 53059, 'Validation failed: dbo.ConsumerRequestTopics does not exist.', 1;

if object_id(N'dbo.RequestResponses', N'U') is null
    throw 53060, 'Validation failed: dbo.RequestResponses does not exist.', 1;

if object_id(N'dbo.OutstandingRequestResponses', N'U') is null
    throw 53061, 'Validation failed: dbo.OutstandingRequestResponses does not exist.', 1;

if object_id(N'dbo.Notifications', N'U') is null
    throw 53028, 'Validation failed: dbo.Notifications does not exist.', 1;

if object_id(N'dbo.NotificationTopics', N'U') is null
    throw 53029, 'Validation failed: dbo.NotificationTopics does not exist.', 1;

if object_id(N'dbo.PublicationNotifications', N'U') is not null
    throw 53053, 'Validation failed: obsolete dbo.PublicationNotifications table still exists.', 1;

if object_id(N'dbo.PublicationNotificationTopics', N'U') is not null
    throw 53054, 'Validation failed: obsolete dbo.PublicationNotificationTopics table still exists.', 1;

if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'PhaseOne-ChannelPersistence')
    throw 53003, 'Validation failed: expected schema version PhaseOne-ChannelPersistence was not recorded.', 1;

if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'RequestServices-001')
    throw 53062, 'Validation failed: expected schema version RequestServices-001 was not recorded.', 1;

if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'RequestResponses-001')
    throw 53063, 'Validation failed: expected schema version RequestResponses-001 was not recorded.', 1;

if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'RequestResponseMessageContentPayload-001')
    throw 53073, 'Validation failed: expected schema version RequestResponseMessageContentPayload-001 was not recorded.', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.RequestResponses')
      and name = N'Payload'
      and system_type_id = type_id(N'varbinary')
      and max_length = -1
      and is_nullable = 1
)
    throw 53074, 'Validation failed: RequestResponses.Payload must be nullable varbinary(max).', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.RequestResponses')
      and name = N'PayloadKind'
      and system_type_id = type_id(N'nvarchar')
      and max_length = 32
      and is_nullable = 1
)
    throw 53075, 'Validation failed: RequestResponses.PayloadKind must be nullable nvarchar(16).', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.RequestResponses')
      and name = N'ContentEncoding'
      and system_type_id = type_id(N'nvarchar')
      and max_length = 128
      and is_nullable = 1
)
    throw 53076, 'Validation failed: RequestResponses.ContentEncoding must be nullable nvarchar(64).', 1;

if not exists
(
    select 1
    from sys.check_constraints
    where parent_object_id = object_id(N'dbo.RequestResponses')
      and name = N'CK_RequestResponses_MessageContentPayloadKind'
      and is_disabled = 0
)
    throw 53077, 'Validation failed: CK_RequestResponses_MessageContentPayloadKind is missing or disabled.', 1;

if not exists
(
    select 1
    from sys.check_constraints
    where parent_object_id = object_id(N'dbo.RequestResponses')
      and name = N'CK_RequestResponses_MessageContentPayloadShape'
      and is_disabled = 0
)
    throw 53078, 'Validation failed: CK_RequestResponses_MessageContentPayloadShape is missing or disabled.', 1;

if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'RequestNotifications-001')
    throw 53064, 'Validation failed: expected schema version RequestNotifications-001 was not recorded.', 1;

if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'SessionNotificationModel-001')
    throw 53065, 'Validation failed: expected schema version SessionNotificationModel-001 was not recorded.', 1;

if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'ResponseNotifications-001')
    throw 53069, 'Validation failed: expected schema version ResponseNotifications-001 was not recorded.', 1;

if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'SessionFilterExpressions-001')
    throw 53079, 'Validation failed: expected schema version SessionFilterExpressions-001 was not recorded.', 1;

if not exists (select 1 from sys.key_constraints where parent_object_id = object_id(N'dbo.Channels') and name = N'PK_Channels' and type = N'PK')
    throw 53004, 'Validation failed: PK_Channels is missing.', 1;

if not exists (select 1 from sys.key_constraints where parent_object_id = object_id(N'dbo.ChannelSecurityTokens') and name = N'PK_ChannelSecurityTokens' and type = N'PK')
    throw 53005, 'Validation failed: PK_ChannelSecurityTokens is missing.', 1;

if not exists
(
    select 1
    from sys.foreign_keys
    where name = N'FK_ChannelSecurityTokens_Channels'
      and parent_object_id = object_id(N'dbo.ChannelSecurityTokens')
      and referenced_object_id = object_id(N'dbo.Channels')
      and delete_referential_action = 0
)
    throw 53006, 'Validation failed: FK_ChannelSecurityTokens_Channels is missing or has cascade delete enabled.', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.Channels')
      and name = N'ChannelUri'
      and system_type_id = type_id(N'nvarchar')
      and max_length = 1024
      and collation_name = N'Latin1_General_100_CS_AS_SC'
)
    throw 53007, 'Validation failed: Channels.ChannelUri must be nvarchar(512) with Latin1_General_100_CS_AS_SC collation.', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.Channels')
      and name = N'Description'
      and system_type_id = type_id(N'nvarchar')
      and max_length = 4000
      and is_nullable = 1
)
    throw 53008, 'Validation failed: Channels.Description must be nullable nvarchar(2000).', 1;

if not exists
(
    select 1 from sys.columns
    where object_id = object_id(N'dbo.ChannelSecurityTokens')
      and name = N'Username'
      and system_type_id = type_id(N'nvarchar')
      and max_length = 512
      and is_nullable = 0
)
    throw 53009, 'Validation failed: ChannelSecurityTokens.Username must be non-null nvarchar(256).', 1;

if not exists
(
    select 1 from sys.columns
    where object_id = object_id(N'dbo.ChannelSecurityTokens')
      and name = N'NormalizedUsername'
      and system_type_id = type_id(N'nvarchar')
      and max_length = 512
      and is_nullable = 0
)
    throw 53010, 'Validation failed: ChannelSecurityTokens.NormalizedUsername must be non-null nvarchar(256).', 1;

if not exists
(
    select 1 from sys.columns
    where object_id = object_id(N'dbo.ChannelSecurityTokens')
      and name = N'PasswordHash'
      and system_type_id = type_id(N'nvarchar')
      and max_length = 1024
      and is_nullable = 0
)
    throw 53011, 'Validation failed: ChannelSecurityTokens.PasswordHash must be non-null nvarchar(512).', 1;

if exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.ChannelSecurityTokens')
      and name in (N'Password', N'Secret', N'PlaintextPassword', N'PasswordPlaintext')
)
    throw 53012, 'Validation failed: plaintext password/secret column exists.', 1;

if not exists
(
    select 1
    from sys.indexes
    where object_id = object_id(N'dbo.Channels')
      and name = N'UQ_Channels_ChannelUri'
      and is_unique = 1
      and has_filter = 1
      and filter_definition like N'%DeletedUtc%is null%'
)
    throw 53013, 'Validation failed: filtered unique index UQ_Channels_ChannelUri is missing or has the wrong filter.', 1;

if not exists
(
    select 1
    from sys.indexes
    where object_id = object_id(N'dbo.ChannelSecurityTokens')
      and name = N'UQ_ChannelSecurityTokens_ChannelId_TokenType_NormalizedUsername'
      and is_unique = 1
      and has_filter = 1
      and filter_definition like N'%DeletedUtc%is null%'
)
    throw 53014, 'Validation failed: filtered unique index UQ_ChannelSecurityTokens_ChannelId_TokenType_NormalizedUsername is missing or has the wrong filter.', 1;

if not exists (select 1 from sys.check_constraints where parent_object_id = object_id(N'dbo.Channels') and name = N'CK_Channels_ChannelType' and is_disabled = 0)
    throw 53015, 'Validation failed: CK_Channels_ChannelType is missing or disabled.', 1;

if not exists (select 1 from sys.check_constraints where parent_object_id = object_id(N'dbo.Channels') and name = N'CK_Channels_DeleteAfterCreate' and is_disabled = 0)
    throw 53016, 'Validation failed: CK_Channels_DeleteAfterCreate is missing or disabled.', 1;

if not exists (select 1 from sys.check_constraints where parent_object_id = object_id(N'dbo.ChannelSecurityTokens') and name = N'CK_ChannelSecurityTokens_TokenType' and is_disabled = 0)
    throw 53017, 'Validation failed: CK_ChannelSecurityTokens_TokenType is missing or disabled.', 1;

if not exists (select 1 from sys.check_constraints where parent_object_id = object_id(N'dbo.ChannelSecurityTokens') and name = N'CK_ChannelSecurityTokens_DeleteAfterCreate' and is_disabled = 0)
    throw 53018, 'Validation failed: CK_ChannelSecurityTokens_DeleteAfterCreate is missing or disabled.', 1;

if not exists (select 1 from sys.default_constraints where parent_object_id = object_id(N'dbo.Channels') and name = N'DF_Channels_ChannelId')
    throw 53019, 'Validation failed: DF_Channels_ChannelId is missing.', 1;

if not exists (select 1 from sys.default_constraints where parent_object_id = object_id(N'dbo.Channels') and name = N'DF_Channels_CreatedUtc')
    throw 53020, 'Validation failed: DF_Channels_CreatedUtc is missing.', 1;

if not exists (select 1 from sys.default_constraints where parent_object_id = object_id(N'dbo.ChannelSecurityTokens') and name = N'DF_ChannelSecurityTokens_ChannelSecurityTokenId')
    throw 53021, 'Validation failed: DF_ChannelSecurityTokens_ChannelSecurityTokenId is missing.', 1;

if not exists (select 1 from sys.default_constraints where parent_object_id = object_id(N'dbo.ChannelSecurityTokens') and name = N'DF_ChannelSecurityTokens_CreatedUtc')
    throw 53022, 'Validation failed: DF_ChannelSecurityTokens_CreatedUtc is missing.', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.ConsumerSubscriptionSessions')
      and name = N'ListenerUrl'
      and system_type_id = type_id(N'nvarchar')
      and max_length = 4096
      and is_nullable = 1
)
    throw 53027, 'Validation failed: ConsumerSubscriptionSessions.ListenerUrl must be nullable nvarchar(2048).', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.ConsumerSubscriptionSessions')
      and name = N'FilterExpressionsJson'
      and system_type_id = type_id(N'nvarchar')
      and max_length = -1
      and is_nullable = 1
)
    throw 53080, 'Validation failed: ConsumerSubscriptionSessions.FilterExpressionsJson must be nullable nvarchar(max).', 1;

if not exists
(
    select 1
    from sys.check_constraints
    where parent_object_id = object_id(N'dbo.ConsumerSubscriptionSessions')
      and name = N'CK_ConsumerSubscriptionSessions_FilterExpressionsJson'
      and definition like N'%isjson%'
)
    throw 53081, 'Validation failed: ConsumerSubscriptionSessions filterExpressions JSON constraint is missing.', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.ProviderRequestSessions')
      and name = N'FilterExpressionsJson'
      and system_type_id = type_id(N'nvarchar')
      and max_length = -1
      and is_nullable = 1
)
    throw 53082, 'Validation failed: ProviderRequestSessions.FilterExpressionsJson must be nullable nvarchar(max).', 1;

if not exists
(
    select 1
    from sys.check_constraints
    where parent_object_id = object_id(N'dbo.ProviderRequestSessions')
      and name = N'CK_ProviderRequestSessions_FilterExpressionsJson'
      and definition like N'%isjson%'
)
    throw 53083, 'Validation failed: ProviderRequestSessions filterExpressions JSON constraint is missing.', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.ConsumerRequestSessions')
      and name = N'ListenerUrl'
      and system_type_id = type_id(N'nvarchar')
      and max_length = 4096
      and is_nullable = 1
)
    throw 53055, 'Validation failed: ConsumerRequestSessions.ListenerUrl must be nullable nvarchar(2048).', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.Notifications')
      and name = N'NotificationServiceType'
      and system_type_id = type_id(N'nvarchar')
      and max_length = 64
      and is_nullable = 0
)
    throw 53043, 'Validation failed: Notifications.NotificationServiceType must be non-null nvarchar(32).', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.Notifications')
      and name = N'ProviderRequestSessionId'
      and system_type_id = type_id(N'uniqueidentifier')
      and is_nullable = 1
)
    throw 53044, 'Validation failed: Notifications.ProviderRequestSessionId must be nullable uniqueidentifier.', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.Notifications')
      and name = N'ConsumerSubscriptionSessionId'
      and system_type_id = type_id(N'uniqueidentifier')
      and is_nullable = 1
)
    throw 53045, 'Validation failed: Notifications.ConsumerSubscriptionSessionId must be nullable uniqueidentifier for mixed notification service types.', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.Notifications')
      and name = N'ConsumerRequestSessionId'
      and system_type_id = type_id(N'uniqueidentifier')
      and is_nullable = 1
)
    throw 53066, 'Validation failed: Notifications.ConsumerRequestSessionId must be nullable uniqueidentifier for future response notification support.', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.Notifications')
      and name = N'RequestMessageId'
      and system_type_id = type_id(N'uniqueidentifier')
      and is_nullable = 1
)
    throw 53070, 'Validation failed: Notifications.RequestMessageId must be nullable uniqueidentifier for response notification request correlation.', 1;

if not exists
(
    select 1
    from sys.check_constraints
    where parent_object_id = object_id(N'dbo.Notifications')
      and name = N'CK_Notifications_ServiceType'
      and definition like N'%Publication%'
      and definition like N'%Request%'
      and definition like N'%Response%'
)
    throw 53046, 'Validation failed: Notifications service-type constraint must allow Publication, Request, and Response.', 1;

if not exists
(
    select 1
    from sys.check_constraints
    where parent_object_id = object_id(N'dbo.Notifications')
      and name = N'CK_Notifications_RecipientShape'
      and definition like N'%ConsumerSubscriptionSessionId%'
      and definition like N'%ProviderRequestSessionId%'
      and definition like N'%ConsumerRequestSessionId%'
      and definition like N'%RequestMessageId%'
      and definition like N'%Publication%'
      and definition like N'%Request%'
      and definition like N'%Response%'
)
    throw 53047, 'Validation failed: Notifications recipient-shape constraint is missing or incomplete for Publication, Request, and Response.', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.Notifications')
      and name = N'ListenerUrl'
      and system_type_id = type_id(N'nvarchar')
      and max_length = 4096
      and is_nullable = 0
)
    throw 53030, 'Validation failed: Notifications.ListenerUrl must be non-null nvarchar(2048).', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.Notifications')
      and name = N'State'
      and system_type_id = type_id(N'nvarchar')
      and max_length = 64
      and is_nullable = 0
)
    throw 53031, 'Validation failed: Notifications.State must be non-null nvarchar(32).', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.Notifications')
      and name = N'LastError'
      and system_type_id = type_id(N'nvarchar')
      and max_length = 4096
      and is_nullable = 1
)
    throw 53036, 'Validation failed: Notifications.LastError must be nullable nvarchar(2048).', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.Notifications')
      and name = N'LockOwner'
      and system_type_id = type_id(N'nvarchar')
      and max_length = 256
      and is_nullable = 1
)
    throw 53037, 'Validation failed: Notifications.LockOwner must be nullable nvarchar(128).', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.Notifications')
      and name = N'ResolutionReason'
      and system_type_id = type_id(N'nvarchar')
      and max_length = 128
      and is_nullable = 1
)
    throw 53038, 'Validation failed: Notifications.ResolutionReason must be nullable nvarchar(64).', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.Notifications')
      and name in (N'LastAttemptUtc', N'LockedUntilUtc')
      and system_type_id = type_id(N'datetime2')
      and is_nullable = 1
    group by object_id
    having count(*) = 2
)
    throw 53039, 'Validation failed: Notifications lease attempt timestamps are missing.', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.Notifications')
      and name = N'DeliveryDeadlineUtc'
      and system_type_id = type_id(N'datetime2')
      and is_nullable = 0
)
    throw 53041, 'Validation failed: Notifications.DeliveryDeadlineUtc must be non-null datetime2.', 1;

if not exists
(
    select 1
    from sys.check_constraints
    where parent_object_id = object_id(N'dbo.Notifications')
      and name = N'CK_Notifications_State'
      and definition like N'%Expired%'
)
    throw 53042, 'Validation failed: Notifications state constraint must allow Expired.', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.Notifications')
      and name = N'LastStatusCode'
      and system_type_id = type_id(N'int')
      and is_nullable = 1
)
    throw 53040, 'Validation failed: Notifications.LastStatusCode must be nullable int.', 1;

if not exists
(
    select 1
    from sys.columns
    where object_id = object_id(N'dbo.NotificationTopics')
      and name = N'TopicValue'
      and system_type_id = type_id(N'nvarchar')
      and max_length = 1024
      and is_nullable = 0
)
    throw 53032, 'Validation failed: NotificationTopics.TopicValue must be non-null nvarchar(512).', 1;

if not exists
(
    select 1
    from sys.indexes
    where object_id = object_id(N'dbo.Notifications')
      and name = N'UQ_Notifications_Message_ConsumerSubscriptionSession_Publication'
      and is_unique = 1
      and has_filter = 1
      and filter_definition like N'%Publication%'
)
    throw 53033, 'Validation failed: UQ_Notifications_Message_ConsumerSubscriptionSession_Publication is missing or is not filtered to publication notifications.', 1;

if not exists
(
    select 1
    from sys.indexes
    where object_id = object_id(N'dbo.Notifications')
      and name = N'UQ_Notifications_Message_ProviderRequestSession_Request'
      and is_unique = 1
      and has_filter = 1
      and filter_definition like N'%Request%'
)
    throw 53048, 'Validation failed: request notification uniqueness index is missing.', 1;

if not exists
(
    select 1
    from sys.indexes indexDefinition
    inner join sys.index_columns messageColumn
            on messageColumn.object_id = indexDefinition.object_id
           and messageColumn.index_id = indexDefinition.index_id
           and messageColumn.key_ordinal = 1
    inner join sys.columns messageColumnDefinition
            on messageColumnDefinition.object_id = messageColumn.object_id
           and messageColumnDefinition.column_id = messageColumn.column_id
    inner join sys.index_columns sessionColumn
            on sessionColumn.object_id = indexDefinition.object_id
           and sessionColumn.index_id = indexDefinition.index_id
           and sessionColumn.key_ordinal = 2
    inner join sys.columns sessionColumnDefinition
            on sessionColumnDefinition.object_id = sessionColumn.object_id
           and sessionColumnDefinition.column_id = sessionColumn.column_id
    where indexDefinition.object_id = object_id(N'dbo.Notifications')
      and indexDefinition.name = N'UQ_Notifications_Message_ConsumerRequestSession_Response'
      and indexDefinition.is_unique = 1
      and indexDefinition.has_filter = 1
      and indexDefinition.filter_definition like N'%Response%'
      and messageColumnDefinition.name = N'MessageId'
      and sessionColumnDefinition.name = N'ConsumerRequestSessionId'
)
    throw 53071, 'Validation failed: response notification uniqueness index must use MessageId and ConsumerRequestSessionId.', 1;

if not exists
(
    select 1
    from sys.indexes
    where object_id = object_id(N'dbo.Notifications')
      and name = N'IX_Notifications_Pending'
      and has_filter = 1
      and filter_definition like N'%Pending%'
      and filter_definition like N'%RetryScheduled%'
      and filter_definition like N'%Sending%'
)
    throw 53034, 'Validation failed: IX_Notifications_Pending is missing or has the wrong filter.', 1;

if not exists
(
    select 1
    from sys.indexes
    where object_id = object_id(N'dbo.Notifications')
      and name = N'IX_Notifications_ProviderRequestSession_State'
      and has_filter = 1
      and filter_definition like N'%Request%'
)
    throw 53050, 'Validation failed: IX_Notifications_ProviderRequestSession_State is missing or has the wrong filter.', 1;

if not exists
(
    select 1
    from sys.foreign_keys
    where parent_object_id = object_id(N'dbo.Notifications')
      and name = N'FK_Notifications_ConsumerSubscriptionSessions'
      and delete_referential_action = 0
)
    throw 53035, 'Validation failed: FK_Notifications_ConsumerSubscriptionSessions is missing or has cascade delete enabled.', 1;

if not exists
(
    select 1
    from sys.foreign_key_columns fkColumn
    inner join sys.foreign_keys fk
            on fk.object_id = fkColumn.constraint_object_id
    inner join sys.columns parentColumn
            on parentColumn.object_id = fkColumn.parent_object_id
           and parentColumn.column_id = fkColumn.parent_column_id
    inner join sys.columns referencedColumn
            on referencedColumn.object_id = fkColumn.referenced_object_id
           and referencedColumn.column_id = fkColumn.referenced_column_id
    where fk.parent_object_id = object_id(N'dbo.Notifications')
      and fk.referenced_object_id = object_id(N'dbo.ProviderPublicationRecipients')
      and fk.name = N'FK_Notifications_PublicationRecipients'
      and fk.delete_referential_action = 0
    group by fk.object_id
    having sum(case when parentColumn.name = N'MessageId' and referencedColumn.name = N'MessageId' then 1 else 0 end) = 1
       and sum(case when parentColumn.name = N'ConsumerSubscriptionSessionId' and referencedColumn.name = N'ConsumerSessionId' then 1 else 0 end) = 1
)
    throw 53068, 'Validation failed: FK_Notifications_PublicationRecipients must map Notifications(MessageId, ConsumerSubscriptionSessionId) to ProviderPublicationRecipients(MessageId, ConsumerSessionId).', 1;

if not exists
(
    select 1
    from sys.foreign_keys
    where parent_object_id = object_id(N'dbo.Notifications')
      and name = N'FK_Notifications_ProviderRequestSessions'
      and delete_referential_action = 0
)
    throw 53049, 'Validation failed: FK_Notifications_ProviderRequestSessions is missing or has cascade delete enabled.', 1;

if not exists
(
    select 1
    from sys.foreign_keys
    where parent_object_id = object_id(N'dbo.Notifications')
      and name = N'FK_Notifications_ConsumerRequestSessions'
      and delete_referential_action = 0
)
    throw 53056, 'Validation failed: FK_Notifications_ConsumerRequestSessions is missing or has cascade delete enabled.', 1;

if not exists
(
    select 1
    from sys.foreign_keys
    where parent_object_id = object_id(N'dbo.Notifications')
      and referenced_object_id = object_id(N'dbo.ConsumerRequests')
      and name = N'FK_Notifications_RequestMessage_ConsumerRequests'
      and delete_referential_action = 0
)
    throw 53072, 'Validation failed: FK_Notifications_RequestMessage_ConsumerRequests must reference dbo.ConsumerRequests without cascade delete.', 1;

if not exists
(
    select 1
    from sys.foreign_keys
    where parent_object_id = object_id(N'dbo.RequestResponses')
      and referenced_object_id = object_id(N'dbo.ProviderRequestSessions')
      and name = N'FK_RequestResponses_ProviderRequestSessions'
      and delete_referential_action = 0
)
    throw 53067, 'Validation failed: FK_RequestResponses_ProviderRequestSessions must reference dbo.ProviderRequestSessions without cascade delete.', 1;

if exists
(
    select ChannelUri collate Latin1_General_100_CS_AS_SC
    from dbo.Channels
    where DeletedUtc is null
    group by ChannelUri collate Latin1_General_100_CS_AS_SC
    having count(*) > 1
)
    throw 53023, 'Validation failed: active exact duplicate ChannelUri values exist.', 1;

if N'/Plant/A' collate Latin1_General_100_CS_AS_SC = N'/plant/a' collate Latin1_General_100_CS_AS_SC
    throw 53024, 'Validation failed: selected ChannelUri collation does not treat /Plant/A and /plant/a as distinct.', 1;

if object_id(N'dbo.Sessions', N'U') is not null
    or object_id(N'dbo.Publications', N'U') is not null
    or object_id(N'dbo.Topics', N'U') is not null
    or object_id(N'dbo.TransportBindings', N'U') is not null
    or object_id(N'dbo.OutboxMessages', N'U') is not null
begin
    throw 53025, 'Validation failed: future-phase table exists in phase-one schema.', 1;
end;

print 'Phase-one schema validation passed.';
go
