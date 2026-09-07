/*
    RapidRedPanda ISBM Host
    Refactors provider request sessions into dedicated tables and generalizes notification table names.
*/

set nocount on;
set xact_abort on;
set quoted_identifier on;

begin try
    begin transaction;

    if object_id(N'dbo.SchemaVersions', N'U') is null
        throw 53300, 'dbo.SchemaVersions does not exist. Apply the phase-one schema before this migration.', 1;

    if exists (select 1 from dbo.SchemaVersions where VersionNumber = N'SessionNotificationModel-001')
    begin
        print 'SessionNotificationModel-001 already recorded. No schema changes were made.';
        commit transaction;
        return;
    end;

    if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'RequestNotifications-001')
        throw 53301, 'Request notification migration has not been recorded. Apply 013_AddRequestNotifications.sql first.', 1;

    if object_id(N'dbo.ProviderRequestSessions', N'U') is null
    begin
        create table dbo.ProviderRequestSessions
        (
            SessionId uniqueidentifier not null,
            ChannelId uniqueidentifier not null,
            SessionState nvarchar(32) not null,
            AzureTopicName nvarchar(260) not null,
            AzureSubscriptionName nvarchar(260) not null,
            AuthenticatedTokenUsername nvarchar(256) null,
            ListenerUrl nvarchar(2048) null,
            OpenedUtc datetime2(7) not null,
            ClosedUtc datetime2(7) null,
            RowVersion rowversion not null,

            constraint PK_ProviderRequestSessions
                primary key clustered (SessionId),

            constraint FK_ProviderRequestSessions_Channels
                foreign key (ChannelId)
                references dbo.Channels (ChannelId),

            constraint CK_ProviderRequestSessions_SessionState
                check (SessionState in (N'Open', N'Closed')),

            constraint CK_ProviderRequestSessions_ClosedAfterOpened
                check (ClosedUtc is null or ClosedUtc >= OpenedUtc)
        );
    end;

    if object_id(N'dbo.ProviderRequestSessionTopics', N'U') is null
    begin
        create table dbo.ProviderRequestSessionTopics
        (
            ProviderRequestSessionTopicId uniqueidentifier not null
                constraint DF_ProviderRequestSessionTopics_Id default newid(),
            ProviderRequestSessionId uniqueidentifier not null,
            TopicOrdinal int not null,
            TopicValue nvarchar(512) not null,
            CreatedUtc datetime2(7) not null
                constraint DF_ProviderRequestSessionTopics_CreatedUtc default sysutcdatetime(),
            RowVersion rowversion not null,

            constraint PK_ProviderRequestSessionTopics
                primary key clustered (ProviderRequestSessionTopicId),

            constraint FK_ProviderRequestSessionTopics_Sessions
                foreign key (ProviderRequestSessionId)
                references dbo.ProviderRequestSessions (SessionId),

            constraint CK_ProviderRequestSessionTopics_TopicOrdinal
                check (TopicOrdinal >= 0)
        );
    end;

    exec sys.sp_executesql N'
        insert dbo.ProviderRequestSessions
        (
            SessionId,
            ChannelId,
            SessionState,
            AzureTopicName,
            AzureSubscriptionName,
            AuthenticatedTokenUsername,
            ListenerUrl,
            OpenedUtc,
            ClosedUtc
        )
        select
            SessionId,
            ChannelId,
            SessionState,
            AzureTopicName,
            AzureSubscriptionName,
            AuthenticatedTokenUsername,
            ListenerUrl,
            OpenedUtc,
            ClosedUtc
        from dbo.ConsumerSubscriptionSessions
        where SessionType = N''ProviderRequestSubscription''
          and not exists
          (
              select 1
              from dbo.ProviderRequestSessions existing
              where existing.SessionId = ConsumerSubscriptionSessions.SessionId
          );
    ';

    exec sys.sp_executesql N'
        insert dbo.ProviderRequestSessionTopics
        (
            ProviderRequestSessionId,
            TopicOrdinal,
            TopicValue,
            CreatedUtc
        )
        select
            topic.SessionId,
            topic.TopicOrdinal,
            topic.TopicValue,
            topic.CreatedUtc
        from dbo.ConsumerSubscriptionSessionTopics topic
        inner join dbo.ConsumerSubscriptionSessions session
                on session.SessionId = topic.SessionId
        where session.SessionType = N''ProviderRequestSubscription''
          and not exists
          (
              select 1
              from dbo.ProviderRequestSessionTopics existing
              where existing.ProviderRequestSessionId = topic.SessionId
                and existing.TopicValue = topic.TopicValue
          );
    ';

    if col_length(N'dbo.ConsumerRequestSessions', N'ListenerUrl') is null
    begin
        alter table dbo.ConsumerRequestSessions
        add ListenerUrl nvarchar(2048) null;
    end;

    if object_id(N'dbo.Notifications', N'U') is null and object_id(N'dbo.PublicationNotifications', N'U') is not null
        exec sys.sp_rename N'dbo.PublicationNotifications', N'Notifications';

    if object_id(N'dbo.NotificationTopics', N'U') is null and object_id(N'dbo.PublicationNotificationTopics', N'U') is not null
        exec sys.sp_rename N'dbo.PublicationNotificationTopics', N'NotificationTopics';

    if exists (select 1 from sys.foreign_keys where parent_object_id = object_id(N'dbo.Notifications') and name = N'FK_PublicationNotifications_Recipients')
        alter table dbo.Notifications drop constraint FK_PublicationNotifications_Recipients;

    if exists (select 1 from sys.foreign_keys where parent_object_id = object_id(N'dbo.Notifications') and name = N'FK_Notifications_PublicationRecipients')
        alter table dbo.Notifications drop constraint FK_Notifications_PublicationRecipients;

    if exists (select 1 from sys.foreign_keys where parent_object_id = object_id(N'dbo.Notifications') and name = N'FK_PublicationNotifications_ProviderRequestSessions')
        alter table dbo.Notifications drop constraint FK_PublicationNotifications_ProviderRequestSessions;

    if exists (select 1 from sys.foreign_keys where parent_object_id = object_id(N'dbo.Notifications') and name = N'FK_Notifications_ProviderRequestSessions')
        alter table dbo.Notifications drop constraint FK_Notifications_ProviderRequestSessions;

    if exists (select 1 from sys.foreign_keys where parent_object_id = object_id(N'dbo.Notifications') and name = N'FK_Notifications_ConsumerSubscriptionSessions')
        alter table dbo.Notifications drop constraint FK_Notifications_ConsumerSubscriptionSessions;

    if exists (select 1 from sys.foreign_keys where parent_object_id = object_id(N'dbo.Notifications') and name = N'FK_Notifications_ConsumerRequestSessions')
        alter table dbo.Notifications drop constraint FK_Notifications_ConsumerRequestSessions;

    if exists (select 1 from sys.foreign_keys where parent_object_id = object_id(N'dbo.NotificationTopics') and name = N'FK_PublicationNotificationTopics_Notifications')
        alter table dbo.NotificationTopics drop constraint FK_PublicationNotificationTopics_Notifications;

    if exists (select 1 from sys.foreign_keys where parent_object_id = object_id(N'dbo.NotificationTopics') and name = N'FK_NotificationTopics_Notifications')
        alter table dbo.NotificationTopics drop constraint FK_NotificationTopics_Notifications;

    if exists (select 1 from sys.foreign_keys where parent_object_id = object_id(N'dbo.RequestResponses') and name = N'FK_RequestResponses_ProviderRequestSessions')
        alter table dbo.RequestResponses drop constraint FK_RequestResponses_ProviderRequestSessions;

    if exists (select 1 from sys.check_constraints where parent_object_id = object_id(N'dbo.Notifications') and name = N'CK_PublicationNotifications_RecipientShape')
        alter table dbo.Notifications drop constraint CK_PublicationNotifications_RecipientShape;

    if exists (select 1 from sys.check_constraints where parent_object_id = object_id(N'dbo.Notifications') and name = N'CK_Notifications_RecipientShape')
        alter table dbo.Notifications drop constraint CK_Notifications_RecipientShape;

    if exists (select 1 from sys.check_constraints where parent_object_id = object_id(N'dbo.Notifications') and name = N'CK_PublicationNotifications_ServiceType')
        alter table dbo.Notifications drop constraint CK_PublicationNotifications_ServiceType;

    if exists (select 1 from sys.check_constraints where parent_object_id = object_id(N'dbo.Notifications') and name = N'CK_Notifications_ServiceType')
        alter table dbo.Notifications drop constraint CK_Notifications_ServiceType;

    if exists (select 1 from sys.check_constraints where parent_object_id = object_id(N'dbo.Notifications') and name = N'CK_PublicationNotifications_State')
        alter table dbo.Notifications drop constraint CK_PublicationNotifications_State;

    if exists (select 1 from sys.check_constraints where parent_object_id = object_id(N'dbo.Notifications') and name = N'CK_Notifications_State')
        alter table dbo.Notifications drop constraint CK_Notifications_State;

    if exists (select 1 from sys.indexes where object_id = object_id(N'dbo.Notifications') and name = N'UQ_PublicationNotifications_Message_ConsumerSession')
        drop index UQ_PublicationNotifications_Message_ConsumerSession on dbo.Notifications;

    if exists (select 1 from sys.indexes where object_id = object_id(N'dbo.Notifications') and name = N'UQ_Notifications_Message_ConsumerSubscriptionSession_Publication')
        drop index UQ_Notifications_Message_ConsumerSubscriptionSession_Publication on dbo.Notifications;

    if exists (select 1 from sys.indexes where object_id = object_id(N'dbo.Notifications') and name = N'UQ_PublicationNotifications_Message_ProviderRequestSession_Request')
        drop index UQ_PublicationNotifications_Message_ProviderRequestSession_Request on dbo.Notifications;

    if exists (select 1 from sys.indexes where object_id = object_id(N'dbo.Notifications') and name = N'UQ_Notifications_Message_ProviderRequestSession_Request')
        drop index UQ_Notifications_Message_ProviderRequestSession_Request on dbo.Notifications;

    if exists (select 1 from sys.indexes where object_id = object_id(N'dbo.Notifications') and name = N'IX_PublicationNotifications_Pending')
        drop index IX_PublicationNotifications_Pending on dbo.Notifications;

    if exists (select 1 from sys.indexes where object_id = object_id(N'dbo.Notifications') and name = N'IX_Notifications_Pending')
        drop index IX_Notifications_Pending on dbo.Notifications;

    if exists (select 1 from sys.indexes where object_id = object_id(N'dbo.Notifications') and name = N'IX_PublicationNotifications_RequestSession_State')
        drop index IX_PublicationNotifications_RequestSession_State on dbo.Notifications;

    if exists (select 1 from sys.indexes where object_id = object_id(N'dbo.Notifications') and name = N'IX_Notifications_ProviderRequestSession_State')
        drop index IX_Notifications_ProviderRequestSession_State on dbo.Notifications;

    if exists (select 1 from sys.indexes where object_id = object_id(N'dbo.Notifications') and name = N'IX_Notifications_ConsumerSubscriptionSession_State')
        drop index IX_Notifications_ConsumerSubscriptionSession_State on dbo.Notifications;

    if col_length(N'dbo.Notifications', N'NotificationId') is null and col_length(N'dbo.Notifications', N'PublicationNotificationId') is not null
        exec sys.sp_rename N'dbo.Notifications.PublicationNotificationId', N'NotificationId', N'COLUMN';

    if col_length(N'dbo.Notifications', N'ConsumerSubscriptionSessionId') is null and col_length(N'dbo.Notifications', N'ConsumerSessionId') is not null
    begin
        alter table dbo.Notifications
        add ConsumerSubscriptionSessionId uniqueidentifier null;
    end;

    if col_length(N'dbo.Notifications', N'ConsumerSessionId') is not null
    begin
        exec sys.sp_executesql N'
            update dbo.Notifications
               set ConsumerSubscriptionSessionId = ConsumerSessionId
             where ConsumerSubscriptionSessionId is null
               and ConsumerSessionId is not null;
        ';
    end;

    if col_length(N'dbo.Notifications', N'ConsumerRequestSessionId') is null
    begin
        alter table dbo.Notifications
        add ConsumerRequestSessionId uniqueidentifier null;
    end;

    if col_length(N'dbo.NotificationTopics', N'NotificationId') is null and col_length(N'dbo.NotificationTopics', N'PublicationNotificationId') is not null
        exec sys.sp_rename N'dbo.NotificationTopics.PublicationNotificationId', N'NotificationId', N'COLUMN';

    exec sys.sp_executesql N'
        alter table dbo.Notifications
        add constraint FK_Notifications_ConsumerSubscriptionSessions
            foreign key (ConsumerSubscriptionSessionId)
            references dbo.ConsumerSubscriptionSessions (SessionId);

        alter table dbo.Notifications
        add constraint FK_Notifications_PublicationRecipients
            foreign key (MessageId, ConsumerSubscriptionSessionId)
            references dbo.ProviderPublicationRecipients (MessageId, ConsumerSessionId);

        alter table dbo.Notifications
        add constraint FK_Notifications_ProviderRequestSessions
            foreign key (ProviderRequestSessionId)
            references dbo.ProviderRequestSessions (SessionId);

        alter table dbo.Notifications
        add constraint FK_Notifications_ConsumerRequestSessions
            foreign key (ConsumerRequestSessionId)
            references dbo.ConsumerRequestSessions (SessionId);

        alter table dbo.NotificationTopics
        add constraint FK_NotificationTopics_Notifications
            foreign key (NotificationId)
            references dbo.Notifications (NotificationId);

        alter table dbo.Notifications
        add constraint CK_Notifications_ServiceType
            check (NotificationServiceType in (N''Publication'', N''Request''));

        alter table dbo.Notifications
        add constraint CK_Notifications_RecipientShape
            check
            (
                (
                    NotificationServiceType = N''Publication''
                    and ConsumerSubscriptionSessionId is not null
                    and ProviderRequestSessionId is null
                    and ConsumerRequestSessionId is null
                )
                or
                (
                    NotificationServiceType = N''Request''
                    and ConsumerSubscriptionSessionId is null
                    and ProviderRequestSessionId is not null
                    and ConsumerRequestSessionId is null
                )
            );

        alter table dbo.Notifications
        add constraint CK_Notifications_State
            check (State in (N''Pending'', N''Sending'', N''Delivered'', N''RetryScheduled'', N''Failed'', N''Suppressed'', N''Expired''));

        alter table dbo.RequestResponses
        add constraint FK_RequestResponses_ProviderRequestSessions
            foreign key (ProviderRequestSessionId)
            references dbo.ProviderRequestSessions (SessionId);

        create unique index UQ_Notifications_Message_ConsumerSubscriptionSession_Publication
        on dbo.Notifications (MessageId, ConsumerSubscriptionSessionId)
        where NotificationServiceType = N''Publication'';

        create unique index UQ_Notifications_Message_ProviderRequestSession_Request
        on dbo.Notifications (MessageId, ProviderRequestSessionId)
        where NotificationServiceType = N''Request'';

        create index IX_Notifications_Pending
        on dbo.Notifications (NotificationServiceType, State, NextAttemptUtc, LockedUntilUtc, DeliveryDeadlineUtc, CreatedUtc, NotificationId)
        include (MessageId, ConsumerSubscriptionSessionId, ProviderRequestSessionId, ConsumerRequestSessionId, ListenerUrl, AttemptCount, LockOwner)
        where State in (N''Pending'', N''RetryScheduled'', N''Sending'');

        create index IX_Notifications_ProviderRequestSession_State
        on dbo.Notifications (NotificationServiceType, ProviderRequestSessionId, State)
        include (MessageId, DeliveryDeadlineUtc)
        where NotificationServiceType = N''Request'';

        create index IX_Notifications_ConsumerSubscriptionSession_State
        on dbo.Notifications (NotificationServiceType, ConsumerSubscriptionSessionId, State)
        include (MessageId, DeliveryDeadlineUtc)
        where NotificationServiceType = N''Publication'';
    ';

    if col_length(N'dbo.Notifications', N'ConsumerSessionId') is not null
    begin
        exec sys.sp_executesql N'
            alter table dbo.Notifications
            drop column ConsumerSessionId;
        ';
    end;

    if not exists (select 1 from sys.indexes where object_id = object_id(N'dbo.ProviderRequestSessions') and name = N'IX_ProviderRequestSessions_ChannelId_Open')
        create index IX_ProviderRequestSessions_ChannelId_Open
        on dbo.ProviderRequestSessions (ChannelId, SessionState, OpenedUtc)
        include (ListenerUrl)
        where SessionState = N'Open';

    if not exists (select 1 from sys.indexes where object_id = object_id(N'dbo.ProviderRequestSessions') and name = N'UQ_ProviderRequestSessions_AzureSubscription')
        create unique index UQ_ProviderRequestSessions_AzureSubscription
        on dbo.ProviderRequestSessions (AzureTopicName, AzureSubscriptionName);

    if not exists (select 1 from sys.indexes where object_id = object_id(N'dbo.ProviderRequestSessionTopics') and name = N'UQ_ProviderRequestSessionTopics_Session_Ordinal')
        create unique index UQ_ProviderRequestSessionTopics_Session_Ordinal
        on dbo.ProviderRequestSessionTopics (ProviderRequestSessionId, TopicOrdinal);

    if not exists (select 1 from sys.indexes where object_id = object_id(N'dbo.ProviderRequestSessionTopics') and name = N'UQ_ProviderRequestSessionTopics_Session_TopicValue')
        create unique index UQ_ProviderRequestSessionTopics_Session_TopicValue
        on dbo.ProviderRequestSessionTopics (ProviderRequestSessionId, TopicValue);

    if not exists (select 1 from sys.indexes where object_id = object_id(N'dbo.ProviderRequestSessionTopics') and name = N'IX_ProviderRequestSessionTopics_TopicValue')
        create index IX_ProviderRequestSessionTopics_TopicValue
        on dbo.ProviderRequestSessionTopics (TopicValue)
        include (ProviderRequestSessionId);

    delete topic
    from dbo.ConsumerSubscriptionSessionTopics topic
    inner join dbo.ConsumerSubscriptionSessions session
            on session.SessionId = topic.SessionId
    where session.SessionType = N'ProviderRequestSubscription';

    delete from dbo.ConsumerSubscriptionSessions
    where SessionType = N'ProviderRequestSubscription';

    if exists (select 1 from sys.check_constraints where parent_object_id = object_id(N'dbo.ConsumerSubscriptionSessions') and name = N'CK_ConsumerSubscriptionSessions_SessionType')
        alter table dbo.ConsumerSubscriptionSessions drop constraint CK_ConsumerSubscriptionSessions_SessionType;

    alter table dbo.ConsumerSubscriptionSessions
    add constraint CK_ConsumerSubscriptionSessions_SessionType
        check (SessionType in (N'ConsumerPublicationSubscription'));

    insert dbo.SchemaVersions (VersionNumber, Description, ScriptName)
    values
    (
        N'SessionNotificationModel-001',
        N'Refactors provider request sessions into dedicated tables and generalizes notification table names.',
        N'014_RefactorSessionNotificationModel.sql'
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
