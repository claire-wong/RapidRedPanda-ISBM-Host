/*
    RapidRedPanda ISBM Host
    Adds media-neutral MessageContent payload fields for request-response persistence.
*/

set nocount on;
set xact_abort on;

begin transaction;

begin try
    if object_id(N'dbo.SchemaVersions', N'U') is null
        throw 53500, 'dbo.SchemaVersions does not exist. Apply the phase-one schema before this migration.', 1;

    if exists (select 1 from dbo.SchemaVersions where VersionNumber = N'RequestResponseMessageContentPayload-001')
    begin
        print 'RequestResponseMessageContentPayload-001 already recorded. No schema changes were made.';
        commit transaction;
        return;
    end;

    if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'RequestResponses-001')
        throw 53501, 'Request response persistence migration has not been recorded. Apply 008_AddRequestResponses.sql first.', 1;

    if object_id(N'dbo.RequestResponses', N'U') is null
        throw 53502, 'dbo.RequestResponses does not exist.', 1;

    if col_length(N'dbo.RequestResponses', N'Payload') is null
    begin
        alter table dbo.RequestResponses
        add Payload varbinary(max) null;
    end;

    if col_length(N'dbo.RequestResponses', N'PayloadKind') is null
    begin
        alter table dbo.RequestResponses
        add PayloadKind nvarchar(16) null;
    end;

    if col_length(N'dbo.RequestResponses', N'ContentEncoding') is null
    begin
        alter table dbo.RequestResponses
        add ContentEncoding nvarchar(64) null;
    end;

    if not exists
    (
        select 1
        from sys.check_constraints
        where parent_object_id = object_id(N'dbo.RequestResponses')
          and name = N'CK_RequestResponses_MessageContentPayloadKind'
    )
    begin
        exec sys.sp_executesql N'
            alter table dbo.RequestResponses
            add constraint CK_RequestResponses_MessageContentPayloadKind
                check (PayloadKind is null or PayloadKind in (N''Json'', N''String'', N''Binary''));';
    end;

    if not exists
    (
        select 1
        from sys.check_constraints
        where parent_object_id = object_id(N'dbo.RequestResponses')
          and name = N'CK_RequestResponses_MessageContentPayloadShape'
    )
    begin
        exec sys.sp_executesql N'
            alter table dbo.RequestResponses
            add constraint CK_RequestResponses_MessageContentPayloadShape
                check
                (
                    (
                        Payload is null
                        and PayloadKind is null
                        and ContentEncoding is null
                    )
                    or
                    (
                        Payload is not null
                        and PayloadKind = N''Json''
                        and MediaType is null
                        and ContentEncoding is null
                    )
                    or
                    (
                        Payload is not null
                        and PayloadKind = N''String''
                        and MediaType is not null
                        and ContentEncoding is null
                    )
                    or
                    (
                        Payload is not null
                        and PayloadKind = N''Binary''
                        and ContentEncoding is not null
                    )
                );';
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
        N'RequestResponseMessageContentPayload-001',
        sysutcdatetime(),
        N'Adds media-neutral MessageContent payload fields for request-response persistence.',
        N'016_AddRequestResponseMessageContentPayload.sql'
    );

    commit transaction;
end try
begin catch
    if @@trancount > 0
        rollback transaction;

    throw;
end catch;
