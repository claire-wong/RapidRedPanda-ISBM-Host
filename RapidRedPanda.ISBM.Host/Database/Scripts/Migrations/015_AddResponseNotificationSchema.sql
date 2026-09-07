/*
    RapidRedPanda ISBM Host
    Adds schema support for response notifications.
*/

set nocount on;
set xact_abort on;
set quoted_identifier on;

begin try
    begin transaction;

    if object_id(N'dbo.SchemaVersions', N'U') is null
        throw 53400, 'dbo.SchemaVersions does not exist. Apply the phase-one schema before this migration.', 1;

    if exists (select 1 from dbo.SchemaVersions where VersionNumber = N'ResponseNotifications-001')
    begin
        print 'ResponseNotifications-001 already recorded. No schema changes were made.';
        commit transaction;
        return;
    end;

    if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'SessionNotificationModel-001')
        throw 53401, 'Session notification model migration has not been recorded. Apply 014_RefactorSessionNotificationModel.sql first.', 1;

    if object_id(N'dbo.Notifications', N'U') is null
        throw 53402, 'dbo.Notifications does not exist.', 1;

    if object_id(N'dbo.ConsumerRequests', N'U') is null
        throw 53403, 'dbo.ConsumerRequests does not exist.', 1;

    if object_id(N'dbo.ConsumerRequestSessions', N'U') is null
        throw 53404, 'dbo.ConsumerRequestSessions does not exist.', 1;

    if col_length(N'dbo.Notifications', N'RequestMessageId') is null
    begin
        alter table dbo.Notifications
        add RequestMessageId uniqueidentifier null;
    end;

    if exists (select 1 from sys.check_constraints where parent_object_id = object_id(N'dbo.Notifications') and name = N'CK_Notifications_RecipientShape')
        alter table dbo.Notifications drop constraint CK_Notifications_RecipientShape;

    if exists (select 1 from sys.check_constraints where parent_object_id = object_id(N'dbo.Notifications') and name = N'CK_Notifications_ServiceType')
        alter table dbo.Notifications drop constraint CK_Notifications_ServiceType;

    if exists (select 1 from sys.indexes where object_id = object_id(N'dbo.Notifications') and name = N'UQ_Notifications_Message_ConsumerRequestSession_Response')
        drop index UQ_Notifications_Message_ConsumerRequestSession_Response on dbo.Notifications;

    if exists (select 1 from sys.indexes where object_id = object_id(N'dbo.Notifications') and name = N'IX_Notifications_Pending')
        drop index IX_Notifications_Pending on dbo.Notifications;

    exec sys.sp_executesql N'
        alter table dbo.Notifications
        add constraint CK_Notifications_ServiceType
            check (NotificationServiceType in (N''Publication'', N''Request'', N''Response''));

        alter table dbo.Notifications
        add constraint CK_Notifications_RecipientShape
            check
            (
                (
                    NotificationServiceType = N''Publication''
                    and ConsumerSubscriptionSessionId is not null
                    and ProviderRequestSessionId is null
                    and ConsumerRequestSessionId is null
                    and RequestMessageId is null
                )
                or
                (
                    NotificationServiceType = N''Request''
                    and ConsumerSubscriptionSessionId is null
                    and ProviderRequestSessionId is not null
                    and ConsumerRequestSessionId is null
                    and RequestMessageId is null
                )
                or
                (
                    NotificationServiceType = N''Response''
                    and ConsumerSubscriptionSessionId is null
                    and ProviderRequestSessionId is null
                    and ConsumerRequestSessionId is not null
                    and RequestMessageId is not null
                )
            );

        create unique index UQ_Notifications_Message_ConsumerRequestSession_Response
        on dbo.Notifications (MessageId, ConsumerRequestSessionId)
        where NotificationServiceType = N''Response'';

        create index IX_Notifications_Pending
        on dbo.Notifications (NotificationServiceType, State, NextAttemptUtc, LockedUntilUtc, DeliveryDeadlineUtc, CreatedUtc, NotificationId)
        include (MessageId, RequestMessageId, ConsumerSubscriptionSessionId, ProviderRequestSessionId, ConsumerRequestSessionId, ListenerUrl, AttemptCount, LockOwner)
        where State in (N''Pending'', N''RetryScheduled'', N''Sending'');
    ';

    if not exists (select 1 from sys.foreign_keys where parent_object_id = object_id(N'dbo.Notifications') and name = N'FK_Notifications_RequestMessage_ConsumerRequests')
    begin
        exec sys.sp_executesql N'
            alter table dbo.Notifications
            add constraint FK_Notifications_RequestMessage_ConsumerRequests
                foreign key (RequestMessageId)
                references dbo.ConsumerRequests (MessageId);
        ';
    end;

    insert dbo.SchemaVersions (VersionNumber, Description, ScriptName)
    values
    (
        N'ResponseNotifications-001',
        N'Adds response notification schema shape and request-message correlation.',
        N'015_AddResponseNotificationSchema.sql'
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
