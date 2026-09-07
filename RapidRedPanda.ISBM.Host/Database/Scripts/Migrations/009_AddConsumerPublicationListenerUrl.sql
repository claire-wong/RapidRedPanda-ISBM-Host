/*
    RapidRedPanda ISBM Host
    Adds optional listener URL storage for consumer publication subscription sessions.
*/

set nocount on;
set xact_abort on;
set quoted_identifier on;

begin try
    begin transaction;

    if object_id(N'dbo.SchemaVersions', N'U') is null
        throw 52700, 'dbo.SchemaVersions does not exist. Apply the phase-one schema before this migration.', 1;

    if exists (select 1 from dbo.SchemaVersions where VersionNumber = N'ConsumerPublicationListenerUrl-001')
    begin
        print 'ConsumerPublicationListenerUrl-001 already recorded. No schema changes were made.';
        commit transaction;
        return;
    end;

    if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'ConsumerSubscriptionSessions-001')
        throw 52701, 'Consumer subscription sessions migration has not been recorded. Apply 002_AddConsumerSubscriptionSessions.sql first.', 1;

    if object_id(N'dbo.ConsumerSubscriptionSessions', N'U') is null
        throw 52702, 'dbo.ConsumerSubscriptionSessions does not exist. Apply 002_AddConsumerSubscriptionSessions.sql first.', 1;

    if col_length(N'dbo.ConsumerSubscriptionSessions', N'ListenerUrl') is null
    begin
        alter table dbo.ConsumerSubscriptionSessions
        add ListenerUrl nvarchar(2048) null;
    end;

    insert dbo.SchemaVersions (VersionNumber, Description, ScriptName)
    values
    (
        N'ConsumerPublicationListenerUrl-001',
        N'Optional listener URL storage for consumer publication subscription sessions.',
        N'009_AddConsumerPublicationListenerUrl.sql'
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
