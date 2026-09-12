/*
    RapidRedPanda ISBM Host
    Adds nullable JSON storage for accepted session filterExpressions.
*/

set nocount on;
set xact_abort on;

begin transaction;

begin try
    if object_id(N'dbo.SchemaVersions', N'U') is null
        throw 53600, 'dbo.SchemaVersions does not exist. Apply the phase-one schema before this migration.', 1;

    if exists (select 1 from dbo.SchemaVersions where VersionNumber = N'SessionFilterExpressions-001')
    begin
        print 'SessionFilterExpressions-001 already recorded. No schema changes were made.';
        commit transaction;
        return;
    end;

    if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'SessionNotificationModel-001')
        throw 53601, 'Session notification model migration has not been recorded. Apply 014_RefactorSessionNotificationModel.sql first.', 1;

    if object_id(N'dbo.ConsumerSubscriptionSessions', N'U') is null
        throw 53602, 'dbo.ConsumerSubscriptionSessions does not exist.', 1;

    if object_id(N'dbo.ProviderRequestSessions', N'U') is null
        throw 53603, 'dbo.ProviderRequestSessions does not exist.', 1;

    if col_length(N'dbo.ConsumerSubscriptionSessions', N'FilterExpressionsJson') is null
    begin
        alter table dbo.ConsumerSubscriptionSessions
        add FilterExpressionsJson nvarchar(max) null;
    end;

    if col_length(N'dbo.ProviderRequestSessions', N'FilterExpressionsJson') is null
    begin
        alter table dbo.ProviderRequestSessions
        add FilterExpressionsJson nvarchar(max) null;
    end;

    if not exists
    (
        select 1
        from sys.check_constraints
        where parent_object_id = object_id(N'dbo.ConsumerSubscriptionSessions')
          and name = N'CK_ConsumerSubscriptionSessions_FilterExpressionsJson'
    )
    begin
        exec sys.sp_executesql N'
            alter table dbo.ConsumerSubscriptionSessions
            add constraint CK_ConsumerSubscriptionSessions_FilterExpressionsJson
                check (FilterExpressionsJson is null or isjson(FilterExpressionsJson) = 1);';
    end;

    if not exists
    (
        select 1
        from sys.check_constraints
        where parent_object_id = object_id(N'dbo.ProviderRequestSessions')
          and name = N'CK_ProviderRequestSessions_FilterExpressionsJson'
    )
    begin
        exec sys.sp_executesql N'
            alter table dbo.ProviderRequestSessions
            add constraint CK_ProviderRequestSessions_FilterExpressionsJson
                check (FilterExpressionsJson is null or isjson(FilterExpressionsJson) = 1);';
    end;

    insert dbo.SchemaVersions
    (
        VersionNumber,
        AppliedUtc,
        Description,
        ScriptName
    )
    values
    (
        N'SessionFilterExpressions-001',
        sysutcdatetime(),
        N'Adds nullable JSON storage for accepted session filterExpressions.',
        N'017_AddSessionFilterExpressions.sql'
    );

    commit transaction;
end try
begin catch
    if @@trancount > 0
        rollback transaction;

    throw;
end catch;
