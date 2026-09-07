/*
    RapidRedPanda ISBM Host
    Generalizes durable notification work so publication and request notification records can share infrastructure.
*/

set nocount on;
set xact_abort on;
set quoted_identifier on;

begin try
    begin transaction;

    if object_id(N'dbo.SchemaVersions', N'U') is null
        throw 53200, 'dbo.SchemaVersions does not exist. Apply the phase-one schema before this migration.', 1;

    if exists (select 1 from dbo.SchemaVersions where VersionNumber = N'RequestNotifications-001')
    begin
        print 'RequestNotifications-001 already recorded. No schema changes were made.';
        commit transaction;
        return;
    end;

    if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'RequestServices-001')
        throw 53201, 'Request services migration has not been recorded. Apply 007_AddRequestServices.sql first.', 1;

    if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'PublicationNotifications-002')
        throw 53202, 'Publication notification delivery deadline migration has not been recorded. Apply 012_AddPublicationNotificationDeliveryDeadline.sql first.', 1;

    if object_id(N'dbo.PublicationNotifications', N'U') is null
        throw 53203, 'dbo.PublicationNotifications does not exist. Apply 010_AddPublicationNotifications.sql first.', 1;

    if object_id(N'dbo.ConsumerSubscriptionSessions', N'U') is null
        throw 53204, 'dbo.ConsumerSubscriptionSessions does not exist. Apply 002_AddConsumerSubscriptionSessions.sql first.', 1;

    if col_length(N'dbo.PublicationNotifications', N'NotificationServiceType') is null
    begin
        alter table dbo.PublicationNotifications
        add NotificationServiceType nvarchar(32) null
            constraint DF_PublicationNotifications_ServiceType default N'Publication';
    end;

    exec sys.sp_executesql N'
        update dbo.PublicationNotifications
           set NotificationServiceType = N''Publication''
         where NotificationServiceType is null;
    ';

    if exists
    (
        select 1
        from sys.columns
        where object_id = object_id(N'dbo.PublicationNotifications')
          and name = N'NotificationServiceType'
          and is_nullable = 1
    )
    begin
        exec sys.sp_executesql N'
            alter table dbo.PublicationNotifications
            alter column NotificationServiceType nvarchar(32) not null;
        ';
    end;

    if col_length(N'dbo.PublicationNotifications', N'ProviderRequestSessionId') is null
    begin
        alter table dbo.PublicationNotifications
        add ProviderRequestSessionId uniqueidentifier null;
    end;

    if exists
    (
        select 1
        from sys.foreign_keys
        where parent_object_id = object_id(N'dbo.PublicationNotifications')
          and name = N'FK_PublicationNotifications_Recipients'
    )
    begin
        alter table dbo.PublicationNotifications
        drop constraint FK_PublicationNotifications_Recipients;
    end;

    if exists
    (
        select 1
        from sys.indexes
        where object_id = object_id(N'dbo.PublicationNotifications')
          and name = N'UQ_PublicationNotifications_Message_ConsumerSession'
    )
    begin
        drop index UQ_PublicationNotifications_Message_ConsumerSession on dbo.PublicationNotifications;
    end;

    if exists
    (
        select 1
        from sys.columns
        where object_id = object_id(N'dbo.PublicationNotifications')
          and name = N'ConsumerSessionId'
          and is_nullable = 0
    )
    begin
        alter table dbo.PublicationNotifications
        alter column ConsumerSessionId uniqueidentifier null;
    end;

    alter table dbo.PublicationNotifications
    add constraint FK_PublicationNotifications_Recipients
        foreign key (MessageId, ConsumerSessionId)
        references dbo.ProviderPublicationRecipients (MessageId, ConsumerSessionId);

    if not exists
    (
        select 1
        from sys.foreign_keys
        where parent_object_id = object_id(N'dbo.PublicationNotifications')
          and name = N'FK_PublicationNotifications_ProviderRequestSessions'
    )
    begin
        exec sys.sp_executesql N'
            alter table dbo.PublicationNotifications
            add constraint FK_PublicationNotifications_ProviderRequestSessions
                foreign key (ProviderRequestSessionId)
                references dbo.ConsumerSubscriptionSessions (SessionId);
        ';
    end;

    if not exists
    (
        select 1
        from sys.check_constraints
        where parent_object_id = object_id(N'dbo.PublicationNotifications')
          and name = N'CK_PublicationNotifications_ServiceType'
    )
    begin
        exec sys.sp_executesql N'
            alter table dbo.PublicationNotifications
            add constraint CK_PublicationNotifications_ServiceType
                check (NotificationServiceType in (N''Publication'', N''Request''));
        ';
    end;

    if not exists
    (
        select 1
        from sys.check_constraints
        where parent_object_id = object_id(N'dbo.PublicationNotifications')
          and name = N'CK_PublicationNotifications_RecipientShape'
    )
    begin
        exec sys.sp_executesql N'
            alter table dbo.PublicationNotifications
            add constraint CK_PublicationNotifications_RecipientShape
                check
                (
                    (
                        NotificationServiceType = N''Publication''
                        and ConsumerSessionId is not null
                        and ProviderRequestSessionId is null
                    )
                    or
                    (
                        NotificationServiceType = N''Request''
                        and ConsumerSessionId is null
                        and ProviderRequestSessionId is not null
                    )
                );
        ';
    end;

    exec sys.sp_executesql N'
        create unique index UQ_PublicationNotifications_Message_ConsumerSession
        on dbo.PublicationNotifications (MessageId, ConsumerSessionId)
        where NotificationServiceType = N''Publication'';
    ';

    if not exists
    (
        select 1
        from sys.indexes
        where object_id = object_id(N'dbo.PublicationNotifications')
          and name = N'UQ_PublicationNotifications_Message_ProviderRequestSession_Request'
    )
    begin
        exec sys.sp_executesql N'
            create unique index UQ_PublicationNotifications_Message_ProviderRequestSession_Request
            on dbo.PublicationNotifications (MessageId, ProviderRequestSessionId)
            where NotificationServiceType = N''Request'';
        ';
    end;

    if exists
    (
        select 1
        from sys.indexes
        where object_id = object_id(N'dbo.PublicationNotifications')
          and name = N'IX_PublicationNotifications_Pending'
    )
    begin
        drop index IX_PublicationNotifications_Pending on dbo.PublicationNotifications;
    end;

    exec sys.sp_executesql N'
        create index IX_PublicationNotifications_Pending
        on dbo.PublicationNotifications (NotificationServiceType, State, NextAttemptUtc, LockedUntilUtc, DeliveryDeadlineUtc, CreatedUtc, PublicationNotificationId)
        include (MessageId, ConsumerSessionId, ProviderRequestSessionId, ListenerUrl, AttemptCount, LockOwner)
        where State in (N''Pending'', N''RetryScheduled'', N''Sending'');
    ';

    if not exists
    (
        select 1
        from sys.indexes
        where object_id = object_id(N'dbo.PublicationNotifications')
          and name = N'IX_PublicationNotifications_RequestSession_State'
    )
    begin
        exec sys.sp_executesql N'
            create index IX_PublicationNotifications_RequestSession_State
            on dbo.PublicationNotifications (NotificationServiceType, ProviderRequestSessionId, State)
            include (MessageId, DeliveryDeadlineUtc)
            where NotificationServiceType = N''Request'';
        ';
    end;

    insert dbo.SchemaVersions (VersionNumber, Description, ScriptName)
    values
    (
        N'RequestNotifications-001',
        N'Adds notification service type and request-recipient shape support for shared notification work.',
        N'013_AddRequestNotifications.sql'
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
