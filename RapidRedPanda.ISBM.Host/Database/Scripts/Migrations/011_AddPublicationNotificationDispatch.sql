/*
    RapidRedPanda ISBM Host
    Adds dispatch lease and result fields for publication notifications.
*/

set nocount on;
set xact_abort on;
set quoted_identifier on;

begin try
    begin transaction;

    if object_id(N'dbo.SchemaVersions', N'U') is null
        throw 52900, 'dbo.SchemaVersions does not exist. Apply the phase-one schema before this migration.', 1;

    if exists (select 1 from dbo.SchemaVersions where VersionNumber = N'PublicationNotificationDispatch-001')
    begin
        print 'PublicationNotificationDispatch-001 already recorded. No schema changes were made.';
        commit transaction;
        return;
    end;

    if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'PublicationNotifications-001')
        throw 52901, 'Publication notifications migration has not been recorded. Apply 010_AddPublicationNotifications.sql first.', 1;

    if object_id(N'dbo.PublicationNotifications', N'U') is null
        throw 52902, 'dbo.PublicationNotifications does not exist. Apply 010_AddPublicationNotifications.sql first.', 1;

    if col_length(N'dbo.PublicationNotifications', N'LastAttemptUtc') is null
        alter table dbo.PublicationNotifications add LastAttemptUtc datetime2(7) null;

    if col_length(N'dbo.PublicationNotifications', N'LastStatusCode') is null
        alter table dbo.PublicationNotifications add LastStatusCode int null;

    if col_length(N'dbo.PublicationNotifications', N'LastError') is null
        alter table dbo.PublicationNotifications add LastError nvarchar(2048) null;

    if col_length(N'dbo.PublicationNotifications', N'LockedUntilUtc') is null
        alter table dbo.PublicationNotifications add LockedUntilUtc datetime2(7) null;

    if col_length(N'dbo.PublicationNotifications', N'LockOwner') is null
        alter table dbo.PublicationNotifications add LockOwner nvarchar(128) null;

    if col_length(N'dbo.PublicationNotifications', N'ResolutionReason') is null
        alter table dbo.PublicationNotifications add ResolutionReason nvarchar(64) null;

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
        check (State in (N'Pending', N'Sending', N'Delivered', N'RetryScheduled', N'Failed', N'Suppressed'));

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

    create index IX_PublicationNotifications_Pending
    on dbo.PublicationNotifications (State, NextAttemptUtc, LockedUntilUtc, CreatedUtc, PublicationNotificationId)
    include (MessageId, ConsumerSessionId, ListenerUrl, AttemptCount, LockOwner)
    where State in (N'Pending', N'RetryScheduled', N'Sending');

    insert dbo.SchemaVersions (VersionNumber, Description, ScriptName)
    values
    (
        N'PublicationNotificationDispatch-001',
        N'Dispatch lease, retry, and terminal result fields for publication notifications.',
        N'011_AddPublicationNotificationDispatch.sql'
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
