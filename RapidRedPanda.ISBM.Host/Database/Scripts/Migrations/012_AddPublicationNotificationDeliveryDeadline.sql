/*
    RapidRedPanda ISBM Host
    Adds durable delivery deadlines for publication notifications.
*/

set nocount on;
set xact_abort on;
set quoted_identifier on;

begin try
    begin transaction;

    if object_id(N'dbo.SchemaVersions', N'U') is null
        throw 53100, 'dbo.SchemaVersions does not exist. Apply the phase-one schema before this migration.', 1;

    if exists (select 1 from dbo.SchemaVersions where VersionNumber = N'PublicationNotifications-002')
    begin
        print 'PublicationNotifications-002 already recorded. No schema changes were made.';
        commit transaction;
        return;
    end;

    if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'PublicationNotifications-001')
        throw 53101, 'Publication notifications migration has not been recorded. Apply 010_AddPublicationNotifications.sql first.', 1;

    if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'PublicationNotificationDispatch-001')
        throw 53102, 'Publication notification dispatch migration has not been recorded. Apply 011_AddPublicationNotificationDispatch.sql first.', 1;

    if object_id(N'dbo.PublicationNotifications', N'U') is null
        throw 53103, 'dbo.PublicationNotifications does not exist. Apply 010_AddPublicationNotifications.sql first.', 1;

    if col_length(N'dbo.PublicationNotifications', N'DeliveryDeadlineUtc') is null
    begin
        alter table dbo.PublicationNotifications
        add DeliveryDeadlineUtc datetime2(7) null;
    end;

    exec sys.sp_executesql N'
        if exists
        (
            select 1
            from dbo.PublicationNotifications
            where CreatedUtc is null
              and DeliveryDeadlineUtc is null
        )
        begin
            throw 53104, ''Cannot backfill PublicationNotifications.DeliveryDeadlineUtc because one or more rows have null CreatedUtc.'', 1;
        end;

        update dbo.PublicationNotifications
           set DeliveryDeadlineUtc = dateadd(minute, 1440, CreatedUtc)
         where DeliveryDeadlineUtc is null;
    ';

    exec sys.sp_executesql N'
        if exists
        (
            select 1
            from dbo.PublicationNotifications
            where DeliveryDeadlineUtc is null
        )
        begin
            throw 53105, ''PublicationNotifications.DeliveryDeadlineUtc backfill did not populate every row.'', 1;
        end;
    ';

    if exists
    (
        select 1
        from sys.columns
        where object_id = object_id(N'dbo.PublicationNotifications')
          and name = N'NextAttemptUtc'
          and is_nullable = 0
    )
    begin
        alter table dbo.PublicationNotifications
        alter column NextAttemptUtc datetime2(7) null;
    end;

    if exists
    (
        select 1
        from sys.columns
        where object_id = object_id(N'dbo.PublicationNotifications')
          and name = N'DeliveryDeadlineUtc'
          and is_nullable = 1
    )
    begin
        exec sys.sp_executesql N'
            alter table dbo.PublicationNotifications
            alter column DeliveryDeadlineUtc datetime2(7) not null;
        ';
    end;

    if exists
    (
        select 1
        from sys.check_constraints
        where parent_object_id = object_id(N'dbo.PublicationNotifications')
          and name = N'CK_PublicationNotifications_State'
    )
    begin
        alter table dbo.PublicationNotifications
        drop constraint CK_PublicationNotifications_State;
    end;

    alter table dbo.PublicationNotifications
    add constraint CK_PublicationNotifications_State
        check (State in (N'Pending', N'Sending', N'Delivered', N'RetryScheduled', N'Failed', N'Suppressed', N'Expired'));

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
        on dbo.PublicationNotifications (State, NextAttemptUtc, LockedUntilUtc, DeliveryDeadlineUtc, CreatedUtc, PublicationNotificationId)
        include (MessageId, ConsumerSessionId, ListenerUrl, AttemptCount, LockOwner)
        where State in (N''Pending'', N''RetryScheduled'', N''Sending'');
    ';

    insert dbo.SchemaVersions (VersionNumber, Description, ScriptName)
    values
    (
        N'PublicationNotifications-002',
        N'Delivery deadline and expired terminal state for publication notifications.',
        N'012_AddPublicationNotificationDeliveryDeadline.sql'
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
