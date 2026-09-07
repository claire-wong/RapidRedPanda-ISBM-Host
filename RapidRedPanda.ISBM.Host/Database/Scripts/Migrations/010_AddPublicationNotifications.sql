/*
    RapidRedPanda ISBM Host
    Adds durable pending notification work for consumer publication sessions.
*/

set nocount on;
set xact_abort on;
set quoted_identifier on;

begin try
    begin transaction;

    if object_id(N'dbo.SchemaVersions', N'U') is null
        throw 52800, 'dbo.SchemaVersions does not exist. Apply the phase-one schema before this migration.', 1;

    if exists (select 1 from dbo.SchemaVersions where VersionNumber = N'PublicationNotifications-001')
    begin
        print 'PublicationNotifications-001 already recorded. No schema changes were made.';
        commit transaction;
        return;
    end;

    if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'ProviderPublicationExpiration-001')
        throw 52801, 'Provider publication metadata migration has not been recorded. Apply 006_AddProviderPublicationExpiration.sql first.', 1;

    if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'ConsumerPublicationListenerUrl-001')
        throw 52802, 'Consumer publication listener URL migration has not been recorded. Apply 009_AddConsumerPublicationListenerUrl.sql first.', 1;

    if object_id(N'dbo.PublicationNotifications', N'U') is null
    begin
        create table dbo.PublicationNotifications
        (
            PublicationNotificationId uniqueidentifier not null
                constraint DF_PublicationNotifications_Id default newid(),
            MessageId uniqueidentifier not null,
            ConsumerSessionId uniqueidentifier not null,
            ListenerUrl nvarchar(2048) not null,
            State nvarchar(32) not null,
            AttemptCount int not null
                constraint DF_PublicationNotifications_AttemptCount default 0,
            NextAttemptUtc datetime2(7) not null
                constraint DF_PublicationNotifications_NextAttemptUtc default sysutcdatetime(),
            LastAttemptUtc datetime2(7) null,
            LastStatusCode int null,
            LastError nvarchar(2048) null,
            LockedUntilUtc datetime2(7) null,
            LockOwner nvarchar(128) null,
            ResolutionReason nvarchar(64) null,
            CreatedUtc datetime2(7) not null
                constraint DF_PublicationNotifications_CreatedUtc default sysutcdatetime(),
            ResolvedUtc datetime2(7) null,
            RowVersion rowversion not null
        );
    end;

    if not exists
    (
        select 1
        from sys.key_constraints
        where parent_object_id = object_id(N'dbo.PublicationNotifications')
          and name = N'PK_PublicationNotifications'
    )
    begin
        alter table dbo.PublicationNotifications
        add constraint PK_PublicationNotifications
            primary key clustered (PublicationNotificationId);
    end;

    if not exists
    (
        select 1
        from sys.foreign_keys
        where parent_object_id = object_id(N'dbo.PublicationNotifications')
          and name = N'FK_PublicationNotifications_Recipients'
    )
    begin
        alter table dbo.PublicationNotifications
        add constraint FK_PublicationNotifications_Recipients
            foreign key (MessageId, ConsumerSessionId)
            references dbo.ProviderPublicationRecipients (MessageId, ConsumerSessionId);
    end;

    if not exists
    (
        select 1
        from sys.check_constraints
        where parent_object_id = object_id(N'dbo.PublicationNotifications')
          and name = N'CK_PublicationNotifications_State'
    )
    begin
        alter table dbo.PublicationNotifications
        add constraint CK_PublicationNotifications_State
            check (State in (N'Pending', N'Sending', N'Delivered', N'RetryScheduled', N'Failed', N'Suppressed'));
    end;

    if not exists
    (
        select 1
        from sys.check_constraints
        where parent_object_id = object_id(N'dbo.PublicationNotifications')
          and name = N'CK_PublicationNotifications_AttemptCount'
    )
    begin
        alter table dbo.PublicationNotifications
        add constraint CK_PublicationNotifications_AttemptCount
            check (AttemptCount >= 0);
    end;

    if not exists
    (
        select 1
        from sys.indexes
        where object_id = object_id(N'dbo.PublicationNotifications')
          and name = N'UQ_PublicationNotifications_Message_ConsumerSession'
    )
    begin
        create unique index UQ_PublicationNotifications_Message_ConsumerSession
        on dbo.PublicationNotifications (MessageId, ConsumerSessionId);
    end;

    if not exists
    (
        select 1
        from sys.indexes
        where object_id = object_id(N'dbo.PublicationNotifications')
          and name = N'IX_PublicationNotifications_Pending'
    )
    begin
        create index IX_PublicationNotifications_Pending
        on dbo.PublicationNotifications (State, NextAttemptUtc, LockedUntilUtc, CreatedUtc, PublicationNotificationId)
        include (MessageId, ConsumerSessionId, ListenerUrl, AttemptCount, LockOwner)
        where State in (N'Pending', N'RetryScheduled', N'Sending');
    end;

    if object_id(N'dbo.PublicationNotificationTopics', N'U') is null
    begin
        create table dbo.PublicationNotificationTopics
        (
            PublicationNotificationId uniqueidentifier not null,
            TopicOrdinal int not null,
            TopicValue nvarchar(512) not null
        );
    end;

    if not exists
    (
        select 1
        from sys.key_constraints
        where parent_object_id = object_id(N'dbo.PublicationNotificationTopics')
          and name = N'PK_PublicationNotificationTopics'
    )
    begin
        alter table dbo.PublicationNotificationTopics
        add constraint PK_PublicationNotificationTopics
            primary key clustered (PublicationNotificationId, TopicOrdinal);
    end;

    if not exists
    (
        select 1
        from sys.foreign_keys
        where parent_object_id = object_id(N'dbo.PublicationNotificationTopics')
          and name = N'FK_PublicationNotificationTopics_Notifications'
    )
    begin
        alter table dbo.PublicationNotificationTopics
        add constraint FK_PublicationNotificationTopics_Notifications
            foreign key (PublicationNotificationId)
            references dbo.PublicationNotifications (PublicationNotificationId);
    end;

    if not exists
    (
        select 1
        from sys.indexes
        where object_id = object_id(N'dbo.PublicationNotificationTopics')
          and name = N'UQ_PublicationNotificationTopics_Notification_TopicValue'
    )
    begin
        create unique index UQ_PublicationNotificationTopics_Notification_TopicValue
        on dbo.PublicationNotificationTopics (PublicationNotificationId, TopicValue);
    end;

    insert dbo.SchemaVersions (VersionNumber, Description, ScriptName)
    values
    (
        N'PublicationNotifications-001',
        N'Durable pending publication notification work for listener-enabled consumer publication sessions.',
        N'010_AddPublicationNotifications.sql'
    );

    commit transaction;
end try
begin catch
    if @@trancount > 0
    begin
        rollback transaction;
    end;

    throw;
end catch;
go
