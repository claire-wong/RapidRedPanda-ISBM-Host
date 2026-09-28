/*
    RapidRedPanda ISBM Host
    Uses binary, code-point, case-sensitive comparison for the accepted topic
    domain and prevents duplicate topic values within a consumer request.
    Application validation prohibits surrounding whitespace so SQL Server's
    trailing-space comparison behavior cannot conflict with ordinal semantics.
*/

set nocount on;
set xact_abort on;

begin transaction;

begin try
    if object_id(N'dbo.SchemaVersions', N'U') is null
        throw 53700, 'dbo.SchemaVersions does not exist. Apply the phase-one schema before this migration.', 1;

    if exists (select 1 from dbo.SchemaVersions where VersionNumber = N'TopicValueSemantics-001')
    begin
        print 'TopicValueSemantics-001 already recorded. No schema changes were made.';
        commit transaction;
        return;
    end;

    if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'SessionFilterExpressions-001')
        throw 53701, 'Session filter expressions migration has not been recorded. Apply 017_AddSessionFilterExpressions.sql first.', 1;

    if not exists (select 1 from sys.fn_helpcollations() where name = N'Latin1_General_100_BIN2')
        throw 53702, 'Required collation Latin1_General_100_BIN2 is not available on this SQL Server instance.', 1;

    if exists
    (
        select required.TableName
        from
        (
            values
                (N'ConsumerSubscriptionSessionTopics'),
                (N'ProviderRequestSessionTopics'),
                (N'ConsumerRequestTopics'),
                (N'ProviderPublicationTopics'),
                (N'NotificationTopics')
        ) required(TableName)
        where object_id(N'dbo.' + required.TableName, N'U') is null
    )
        throw 53703, 'One or more required topic tables do not exist.', 1;

    if exists
    (
        select required.TableName
        from
        (
            values
                (N'ConsumerSubscriptionSessionTopics'),
                (N'ProviderRequestSessionTopics'),
                (N'ConsumerRequestTopics'),
                (N'ProviderPublicationTopics'),
                (N'NotificationTopics')
        ) required(TableName)
        left join sys.columns columnDefinition
               on columnDefinition.object_id = object_id(N'dbo.' + required.TableName)
              and columnDefinition.name = N'TopicValue'
              and columnDefinition.system_type_id = type_id(N'nvarchar')
              and columnDefinition.max_length = 1024
              and columnDefinition.is_nullable = 0
        where columnDefinition.column_id is null
    )
        throw 53704, 'Every TopicValue column must be non-null nvarchar(512) before migration.', 1;

    if exists
    (
        select 1
        from dbo.ConsumerSubscriptionSessionTopics
        group by SessionId, TopicValue collate Latin1_General_100_BIN2
        having count(*) > 1
    )
        throw 53705, 'ConsumerSubscriptionSessionTopics contains values that collide under Latin1_General_100_BIN2.', 1;

    if exists
    (
        select 1
        from dbo.ProviderRequestSessionTopics
        group by ProviderRequestSessionId, TopicValue collate Latin1_General_100_BIN2
        having count(*) > 1
    )
        throw 53706, 'ProviderRequestSessionTopics contains values that collide under Latin1_General_100_BIN2.', 1;

    if exists
    (
        select 1
        from dbo.ConsumerRequestTopics
        group by MessageId, TopicValue collate Latin1_General_100_BIN2
        having count(*) > 1
    )
        throw 53707, 'ConsumerRequestTopics contains duplicate values under Latin1_General_100_BIN2.', 1;

    if exists
    (
        select 1
        from dbo.ProviderPublicationTopics
        group by MessageId, TopicValue collate Latin1_General_100_BIN2
        having count(*) > 1
    )
        throw 53708, 'ProviderPublicationTopics contains values that collide under Latin1_General_100_BIN2.', 1;

    if exists
    (
        select 1
        from dbo.NotificationTopics
        group by NotificationId, TopicValue collate Latin1_General_100_BIN2
        having count(*) > 1
    )
        throw 53709, 'NotificationTopics contains values that collide under Latin1_General_100_BIN2.', 1;

    if not exists (select 1 from sys.indexes where object_id = object_id(N'dbo.ConsumerSubscriptionSessionTopics') and name = N'UQ_ConsumerSubscriptionSessionTopics_Session_TopicValue')
       or not exists (select 1 from sys.indexes where object_id = object_id(N'dbo.ConsumerSubscriptionSessionTopics') and name = N'IX_ConsumerSubscriptionSessionTopics_TopicValue')
       or not exists (select 1 from sys.indexes where object_id = object_id(N'dbo.ProviderRequestSessionTopics') and name = N'UQ_ProviderRequestSessionTopics_Session_TopicValue')
       or not exists (select 1 from sys.indexes where object_id = object_id(N'dbo.ProviderRequestSessionTopics') and name = N'IX_ProviderRequestSessionTopics_TopicValue')
       or not exists (select 1 from sys.indexes where object_id = object_id(N'dbo.ProviderPublicationTopics') and name = N'UQ_ProviderPublicationTopics_Message_TopicValue')
       or not exists
          (
              select 1
              from sys.indexes
              where object_id = object_id(N'dbo.NotificationTopics')
                and name in
                (
                    N'UQ_NotificationTopics_Notification_TopicValue',
                    N'UQ_PublicationNotificationTopics_Notification_TopicValue'
                )
          )
        throw 53710, 'One or more required TopicValue indexes are missing.', 1;

    drop index UQ_ConsumerSubscriptionSessionTopics_Session_TopicValue on dbo.ConsumerSubscriptionSessionTopics;
    drop index IX_ConsumerSubscriptionSessionTopics_TopicValue on dbo.ConsumerSubscriptionSessionTopics;
    drop index UQ_ProviderRequestSessionTopics_Session_TopicValue on dbo.ProviderRequestSessionTopics;
    drop index IX_ProviderRequestSessionTopics_TopicValue on dbo.ProviderRequestSessionTopics;
    drop index UQ_ProviderPublicationTopics_Message_TopicValue on dbo.ProviderPublicationTopics;

    if exists (select 1 from sys.indexes where object_id = object_id(N'dbo.NotificationTopics') and name = N'UQ_NotificationTopics_Notification_TopicValue')
        drop index UQ_NotificationTopics_Notification_TopicValue on dbo.NotificationTopics;

    if exists (select 1 from sys.indexes where object_id = object_id(N'dbo.NotificationTopics') and name = N'UQ_PublicationNotificationTopics_Notification_TopicValue')
        drop index UQ_PublicationNotificationTopics_Notification_TopicValue on dbo.NotificationTopics;

    alter table dbo.ConsumerSubscriptionSessionTopics
    alter column TopicValue nvarchar(512) collate Latin1_General_100_BIN2 not null;

    alter table dbo.ProviderRequestSessionTopics
    alter column TopicValue nvarchar(512) collate Latin1_General_100_BIN2 not null;

    alter table dbo.ConsumerRequestTopics
    alter column TopicValue nvarchar(512) collate Latin1_General_100_BIN2 not null;

    alter table dbo.ProviderPublicationTopics
    alter column TopicValue nvarchar(512) collate Latin1_General_100_BIN2 not null;

    alter table dbo.NotificationTopics
    alter column TopicValue nvarchar(512) collate Latin1_General_100_BIN2 not null;

    create unique index UQ_ConsumerSubscriptionSessionTopics_Session_TopicValue
    on dbo.ConsumerSubscriptionSessionTopics (SessionId, TopicValue);

    create index IX_ConsumerSubscriptionSessionTopics_TopicValue
    on dbo.ConsumerSubscriptionSessionTopics (TopicValue)
    include (SessionId);

    create unique index UQ_ProviderRequestSessionTopics_Session_TopicValue
    on dbo.ProviderRequestSessionTopics (ProviderRequestSessionId, TopicValue);

    create index IX_ProviderRequestSessionTopics_TopicValue
    on dbo.ProviderRequestSessionTopics (TopicValue)
    include (ProviderRequestSessionId);

    create unique index UQ_ConsumerRequestTopics_Message_TopicValue
    on dbo.ConsumerRequestTopics (MessageId, TopicValue);

    create unique index UQ_ProviderPublicationTopics_Message_TopicValue
    on dbo.ProviderPublicationTopics (MessageId, TopicValue);

    create unique index UQ_NotificationTopics_Notification_TopicValue
    on dbo.NotificationTopics (NotificationId, TopicValue);

    insert dbo.SchemaVersions
    (
        VersionNumber,
        AppliedUtc,
        Description,
        ScriptName
    )
    values
    (
        N'TopicValueSemantics-001',
        sysutcdatetime(),
        N'Aligns topic equality with binary code-point semantics and prevents duplicate consumer request topics.',
        N'018_HardenTopicValueSemantics.sql'
    );

    commit transaction;
end try
begin catch
    if @@trancount > 0
        rollback transaction;

    throw;
end catch;
