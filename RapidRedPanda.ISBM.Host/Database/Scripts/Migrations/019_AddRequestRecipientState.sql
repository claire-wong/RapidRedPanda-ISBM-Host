/*
    RapidRedPanda ISBM Host
    Adds durable request content and provider request recipient state for restart-safe request lifecycle.
*/

set nocount on;
set xact_abort on;
set quoted_identifier on;

begin transaction;

begin try
    if object_id(N'dbo.SchemaVersions', N'U') is null
        throw 53600, 'dbo.SchemaVersions does not exist. Apply the phase-one schema before this migration.', 1;

    if exists (select 1 from dbo.SchemaVersions where VersionNumber = N'RequestRecipientState-001')
    begin
        print 'RequestRecipientState-001 already recorded. No schema changes were made.';
        commit transaction;
        return;
    end;

    if not exists (select 1 from dbo.SchemaVersions where VersionNumber = N'RequestServices-001')
        throw 53601, 'Request services migration has not been recorded. Apply 007_AddRequestServices.sql first.', 1;

    if object_id(N'dbo.ConsumerRequests', N'U') is null
        throw 53602, 'dbo.ConsumerRequests does not exist.', 1;

    if object_id(N'dbo.ProviderRequestSessions', N'U') is null
        throw 53603, 'dbo.ProviderRequestSessions does not exist.', 1;

    if col_length(N'dbo.ConsumerRequests', N'MessageContent') is null
    begin
        alter table dbo.ConsumerRequests
        add MessageContent nvarchar(max) null;
    end;

    if col_length(N'dbo.ConsumerRequests', N'MediaType') is null
    begin
        alter table dbo.ConsumerRequests
        add MediaType nvarchar(256) null;
    end;

    if col_length(N'dbo.ConsumerRequests', N'Payload') is null
    begin
        alter table dbo.ConsumerRequests
        add Payload varbinary(max) null;
    end;

    if col_length(N'dbo.ConsumerRequests', N'PayloadKind') is null
    begin
        alter table dbo.ConsumerRequests
        add PayloadKind nvarchar(16) null;
    end;

    if col_length(N'dbo.ConsumerRequests', N'ContentEncoding') is null
    begin
        alter table dbo.ConsumerRequests
        add ContentEncoding nvarchar(64) null;
    end;

    if not exists
    (
        select 1
        from sys.check_constraints
        where parent_object_id = object_id(N'dbo.ConsumerRequests')
          and name = N'CK_ConsumerRequests_MessageContentPayloadKind'
    )
    begin
        exec sys.sp_executesql N'
            alter table dbo.ConsumerRequests
            add constraint CK_ConsumerRequests_MessageContentPayloadKind
                check (PayloadKind is null or PayloadKind in (N''Json'', N''String'', N''Binary''));';
    end;

    if not exists
    (
        select 1
        from sys.check_constraints
        where parent_object_id = object_id(N'dbo.ConsumerRequests')
          and name = N'CK_ConsumerRequests_MessageContentPayloadShape'
    )
    begin
        exec sys.sp_executesql N'
            alter table dbo.ConsumerRequests
            add constraint CK_ConsumerRequests_MessageContentPayloadShape
                check
                (
                    (
                        MessageContent is null
                        and Payload is null
                        and PayloadKind is null
                        and ContentEncoding is null
                    )
                    or
                    (
                        MessageContent is not null
                        and Payload is not null
                        and PayloadKind = N''Json''
                        and MediaType is null
                        and ContentEncoding is null
                    )
                    or
                    (
                        MessageContent is not null
                        and Payload is not null
                        and PayloadKind = N''String''
                        and MediaType is not null
                        and ContentEncoding is null
                    )
                    or
                    (
                        MessageContent is not null
                        and Payload is not null
                        and PayloadKind = N''Binary''
                        and ContentEncoding is not null
                    )
                );';
    end;

    if object_id(N'dbo.ConsumerRequestRecipients', N'U') is null
    begin
        create table dbo.ConsumerRequestRecipients
        (
            RequestMessageId uniqueidentifier not null,
            ProviderRequestSessionId uniqueidentifier not null,
            State nvarchar(32) not null,
            TopicValue nvarchar(512) not null,
            ReadUtc datetime2(7) null,
            ResolvedUtc datetime2(7) null,
            CreatedUtc datetime2(7) not null,
            RowVersion rowversion not null
        );
    end;

    if not exists
    (
        select 1
        from sys.default_constraints
        where parent_object_id = object_id(N'dbo.ConsumerRequestRecipients')
          and name = N'DF_ConsumerRequestRecipients_CreatedUtc'
    )
    begin
        alter table dbo.ConsumerRequestRecipients
        add constraint DF_ConsumerRequestRecipients_CreatedUtc
            default sysutcdatetime() for CreatedUtc;
    end;

    if not exists
    (
        select 1
        from sys.key_constraints
        where parent_object_id = object_id(N'dbo.ConsumerRequestRecipients')
          and name = N'PK_ConsumerRequestRecipients'
    )
    begin
        alter table dbo.ConsumerRequestRecipients
        add constraint PK_ConsumerRequestRecipients
            primary key clustered (RequestMessageId, ProviderRequestSessionId);
    end;

    if not exists
    (
        select 1
        from sys.foreign_keys
        where parent_object_id = object_id(N'dbo.ConsumerRequestRecipients')
          and name = N'FK_ConsumerRequestRecipients_Requests'
    )
    begin
        alter table dbo.ConsumerRequestRecipients
        add constraint FK_ConsumerRequestRecipients_Requests
            foreign key (RequestMessageId)
            references dbo.ConsumerRequests (MessageId);
    end;

    if not exists
    (
        select 1
        from sys.foreign_keys
        where parent_object_id = object_id(N'dbo.ConsumerRequestRecipients')
          and name = N'FK_ConsumerRequestRecipients_ProviderRequestSessions'
    )
    begin
        alter table dbo.ConsumerRequestRecipients
        add constraint FK_ConsumerRequestRecipients_ProviderRequestSessions
            foreign key (ProviderRequestSessionId)
            references dbo.ProviderRequestSessions (SessionId);
    end;

    if not exists
    (
        select 1
        from sys.check_constraints
        where parent_object_id = object_id(N'dbo.ConsumerRequestRecipients')
          and name = N'CK_ConsumerRequestRecipients_State'
    )
    begin
        alter table dbo.ConsumerRequestRecipients
        add constraint CK_ConsumerRequestRecipients_State
            check (State in (N'Outstanding', N'Removed'));
    end;

    if not exists
    (
        select 1
        from sys.indexes
        where object_id = object_id(N'dbo.ConsumerRequestRecipients')
          and name = N'UQ_ConsumerRequestRecipients_Provider_Outstanding'
    )
    begin
        create unique index UQ_ConsumerRequestRecipients_Provider_Outstanding
        on dbo.ConsumerRequestRecipients (ProviderRequestSessionId)
        include (RequestMessageId, TopicValue, ReadUtc)
        where State = N'Outstanding';
    end;

    if not exists
    (
        select 1
        from sys.indexes
        where object_id = object_id(N'dbo.ConsumerRequestRecipients')
          and name = N'IX_ConsumerRequestRecipients_Request_State_Provider'
    )
    begin
        create index IX_ConsumerRequestRecipients_Request_State_Provider
        on dbo.ConsumerRequestRecipients (RequestMessageId, State, ProviderRequestSessionId);
    end;

    if not exists
    (
        select 1
        from sys.indexes
        where object_id = object_id(N'dbo.ConsumerRequestRecipients')
          and name = N'IX_ConsumerRequestRecipients_Provider_State'
    )
    begin
        create index IX_ConsumerRequestRecipients_Provider_State
        on dbo.ConsumerRequestRecipients (ProviderRequestSessionId, State, ResolvedUtc)
        include (RequestMessageId, TopicValue);
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
        N'RequestRecipientState-001',
        sysutcdatetime(),
        N'Adds durable request content and provider request recipient state.',
        N'019_AddRequestRecipientState.sql'
    );

    commit transaction;
end try
begin catch
    if @@trancount > 0
        rollback transaction;

    throw;
end catch;
