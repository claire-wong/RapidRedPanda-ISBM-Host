/*
    RapidRedPanda ISBM Host
    Authoritative fresh-install database creation script.

    Before executing in SSMS or Azure Data Studio:
    1. Review the target SQL Server instance.
    2. Set @DatabaseName if the default is not desired.
    3. Execute this script normally. SQLCMD Mode is not required.

    This script creates the database if needed, switches to it, and installs
    the complete current schema through NotificationCleanupIndexes-001.
    It does not specify physical file paths; SQL Server uses its configured
    default data and log locations.
*/

set nocount on;

-- Change this value before running if a different database name is required.
declare @DatabaseName sysname = N'RapidRedPandaISBM';

if @DatabaseName is null or len(ltrim(rtrim(@DatabaseName))) = 0
begin
    throw 51000, 'DatabaseName must be provided.', 1;
end;

if db_id(@DatabaseName) is null
begin
    declare @CreateSql nvarchar(max) = N'create database ' + quotename(@DatabaseName) + N';';
    exec sys.sp_executesql @CreateSql;
    print 'Database created.';
end
else
begin
    declare @StateDesc nvarchar(60);
    declare @UserAccessDesc nvarchar(60);
    declare @IsReadOnly bit;

    select
        @StateDesc = state_desc,
        @UserAccessDesc = user_access_desc,
        @IsReadOnly = is_read_only
    from sys.databases
    where name = @DatabaseName;

    if @StateDesc <> N'ONLINE' or @UserAccessDesc <> N'MULTI_USER' or @IsReadOnly = 1
    begin
        throw 51001, 'A database with the requested name exists, but it is not an online, multi-user, writable database.', 1;
    end;

    print 'Database already exists and appears compatible for schema installation.';
end;

declare @InstallSchemaSql nvarchar(max) = N'use ' + quotename(@DatabaseName) + N';
set nocount on;
set xact_abort on;
set quoted_identifier on;

begin try
    begin transaction;

    if object_id(N''dbo.SchemaVersions'', N''U'') is not null
    begin
        if not exists
        (
            select 1
            from sys.columns
            where object_id = object_id(N''dbo.SchemaVersions'', N''U'')
              and name in (N''SchemaVersionId'', N''VersionNumber'', N''Description'', N''AppliedUtc'', N''ScriptName'')
            group by object_id
            having count(*) = 5
        )
        begin
            throw 51000, ''dbo.SchemaVersions exists but is not compatible with the expected schema.'', 1;
        end;
    end
    else
    begin
        create table dbo.SchemaVersions
        (
            SchemaVersionId int identity(1,1) not null,
            VersionNumber nvarchar(50) not null,
            Description nvarchar(500) not null,
            AppliedUtc datetime2(7) not null
                constraint DF_SchemaVersions_AppliedUtc default sysutcdatetime(),
            ScriptName nvarchar(260) not null,

            constraint PK_SchemaVersions
                primary key clustered (SchemaVersionId),

            constraint UQ_SchemaVersions_VersionNumber
                unique (VersionNumber)
        );
    end;

    if exists (select 1 from dbo.SchemaVersions where VersionNumber = N''PhaseOne-ChannelPersistence'')
    begin
        print ''PhaseOne-ChannelPersistence already recorded. No schema changes were made.'';
        commit transaction;
        return;
    end;

    if object_id(N''dbo.Channels'', N''U'') is not null
    begin
        throw 51001, ''dbo.Channels already exists. Refusing to silently accept or alter an existing object.'', 1;
    end;

    if object_id(N''dbo.ChannelSecurityTokens'', N''U'') is not null
    begin
        throw 51002, ''dbo.ChannelSecurityTokens already exists. Refusing to silently accept or alter an existing object.'', 1;
    end;

    create table dbo.Channels
    (
        ChannelId uniqueidentifier not null
            constraint DF_Channels_ChannelId default newid(),

        ChannelUri nvarchar(512) collate Latin1_General_100_CS_AS_SC not null,
        ChannelType nvarchar(32) not null,
        Description nvarchar(2000) null,
        AzureTopicName nvarchar(260) not null,

        CreatedUtc datetime2(7) not null
            constraint DF_Channels_CreatedUtc default sysutcdatetime(),

        DeletedUtc datetime2(7) null,
        RowVersion rowversion not null,

        constraint PK_Channels
            primary key clustered (ChannelId),

        constraint CK_Channels_ChannelType
            check (ChannelType in (N''Publication'', N''Request'')),

        constraint CK_Channels_DeleteAfterCreate
            check (DeletedUtc is null or DeletedUtc >= CreatedUtc)
    );

    create unique index UQ_Channels_ChannelUri
    on dbo.Channels (ChannelUri)
    where DeletedUtc is null;

    create unique index UQ_Channels_AzureTopicName
    on dbo.Channels (AzureTopicName);

    create index IX_Channels_DeletedUtc
    on dbo.Channels (DeletedUtc);

    create table dbo.ChannelSecurityTokens
    (
        ChannelSecurityTokenId uniqueidentifier not null
            constraint DF_ChannelSecurityTokens_ChannelSecurityTokenId default newid(),

        ChannelId uniqueidentifier not null,
        TokenType nvarchar(64) not null,
        Username nvarchar(256) not null,
        NormalizedUsername nvarchar(256) not null,
        PasswordHash nvarchar(512) not null,

        CreatedUtc datetime2(7) not null
            constraint DF_ChannelSecurityTokens_CreatedUtc default sysutcdatetime(),

        DeletedUtc datetime2(7) null,
        RowVersion rowversion not null,

        constraint PK_ChannelSecurityTokens
            primary key clustered (ChannelSecurityTokenId),

        constraint FK_ChannelSecurityTokens_Channels
            foreign key (ChannelId)
            references dbo.Channels (ChannelId),

        constraint CK_ChannelSecurityTokens_TokenType
            check (TokenType in (N''UsernameToken'')),

        constraint CK_ChannelSecurityTokens_DeleteAfterCreate
            check (DeletedUtc is null or DeletedUtc >= CreatedUtc)
    );

    create unique index UQ_ChannelSecurityTokens_ChannelId_TokenType_NormalizedUsername
    on dbo.ChannelSecurityTokens (ChannelId, TokenType, NormalizedUsername)
    where DeletedUtc is null;

    create index IX_ChannelSecurityTokens_ChannelId
    on dbo.ChannelSecurityTokens (ChannelId)
    where DeletedUtc is null;

    create table dbo.ConsumerSubscriptionSessions
    (
        SessionId uniqueidentifier not null,
        ChannelId uniqueidentifier not null,
        SessionType nvarchar(64) not null,
        SessionState nvarchar(32) not null,
        AzureTopicName nvarchar(260) not null,
        AzureSubscriptionName nvarchar(260) not null,
        AuthenticatedTokenUsername nvarchar(256) null,
        ListenerUrl nvarchar(2048) null,
        FilterExpressionsJson nvarchar(max) null,
        OpenedUtc datetime2(7) not null
            constraint DF_ConsumerSubscriptionSessions_OpenedUtc default sysutcdatetime(),
        ClosedUtc datetime2(7) null,
        RowVersion rowversion not null,

        constraint PK_ConsumerSubscriptionSessions
            primary key clustered (SessionId),

        constraint FK_ConsumerSubscriptionSessions_Channels
            foreign key (ChannelId)
            references dbo.Channels (ChannelId),

        constraint CK_ConsumerSubscriptionSessions_SessionType
            check (SessionType in (N''ConsumerPublicationSubscription'')),

        constraint CK_ConsumerSubscriptionSessions_SessionState
            check (SessionState in (N''Open'', N''Closed'')),

        constraint CK_ConsumerSubscriptionSessions_ClosedAfterOpened
            check (ClosedUtc is null or ClosedUtc >= OpenedUtc),

        constraint CK_ConsumerSubscriptionSessions_FilterExpressionsJson
            check (FilterExpressionsJson is null or isjson(FilterExpressionsJson) = 1)
    );

    create index IX_ConsumerSubscriptionSessions_ChannelId_Open
    on dbo.ConsumerSubscriptionSessions (ChannelId, SessionState)
    include (AzureTopicName, AzureSubscriptionName)
    where SessionState = N''Open'';

    create index IX_ConsumerSubscriptionSessions_ChannelId
    on dbo.ConsumerSubscriptionSessions (ChannelId)
    include
    (
        SessionId,
        SessionType,
        SessionState,
        AzureTopicName,
        AzureSubscriptionName,
        AuthenticatedTokenUsername,
        OpenedUtc,
        ClosedUtc,
        ListenerUrl
    );

    create unique index UQ_ConsumerSubscriptionSessions_AzureSubscription
    on dbo.ConsumerSubscriptionSessions (AzureTopicName, AzureSubscriptionName);

    create table dbo.ConsumerSubscriptionSessionTopics
    (
        ConsumerSubscriptionSessionTopicId uniqueidentifier not null
            constraint DF_ConsumerSubscriptionSessionTopics_Id default newid(),
        SessionId uniqueidentifier not null,
        TopicOrdinal int not null,
        TopicValue nvarchar(512) collate Latin1_General_100_BIN2 not null,
        CreatedUtc datetime2(7) not null
            constraint DF_ConsumerSubscriptionSessionTopics_CreatedUtc default sysutcdatetime(),
        RowVersion rowversion not null,

        constraint PK_ConsumerSubscriptionSessionTopics
            primary key clustered (ConsumerSubscriptionSessionTopicId),

        constraint FK_ConsumerSubscriptionSessionTopics_Sessions
            foreign key (SessionId)
            references dbo.ConsumerSubscriptionSessions (SessionId),

        constraint CK_ConsumerSubscriptionSessionTopics_TopicOrdinal
            check (TopicOrdinal >= 0)
    );

    create unique index UQ_ConsumerSubscriptionSessionTopics_Session_Ordinal
    on dbo.ConsumerSubscriptionSessionTopics (SessionId, TopicOrdinal);

    create unique index UQ_ConsumerSubscriptionSessionTopics_Session_TopicValue
    on dbo.ConsumerSubscriptionSessionTopics (SessionId, TopicValue);

    create index IX_ConsumerSubscriptionSessionTopics_TopicValue
    on dbo.ConsumerSubscriptionSessionTopics (TopicValue)
    include (SessionId);

    create table dbo.ProviderRequestSessions
    (
        SessionId uniqueidentifier not null,
        ChannelId uniqueidentifier not null,
        SessionState nvarchar(32) not null,
        AzureTopicName nvarchar(260) not null,
        AzureSubscriptionName nvarchar(260) not null,
        AuthenticatedTokenUsername nvarchar(256) null,
        ListenerUrl nvarchar(2048) null,
        FilterExpressionsJson nvarchar(max) null,
        OpenedUtc datetime2(7) not null
            constraint DF_ProviderRequestSessions_OpenedUtc default sysutcdatetime(),
        ClosedUtc datetime2(7) null,
        RowVersion rowversion not null,

        constraint PK_ProviderRequestSessions
            primary key clustered (SessionId),

        constraint FK_ProviderRequestSessions_Channels
            foreign key (ChannelId)
            references dbo.Channels (ChannelId),

        constraint CK_ProviderRequestSessions_SessionState
            check (SessionState in (N''Open'', N''Closed'')),

        constraint CK_ProviderRequestSessions_ClosedAfterOpened
            check (ClosedUtc is null or ClosedUtc >= OpenedUtc),

        constraint CK_ProviderRequestSessions_FilterExpressionsJson
            check (FilterExpressionsJson is null or isjson(FilterExpressionsJson) = 1)
    );

    create index IX_ProviderRequestSessions_ChannelId_Open
    on dbo.ProviderRequestSessions (ChannelId, SessionState, OpenedUtc)
    include (ListenerUrl)
    where SessionState = N''Open'';

    create unique index UQ_ProviderRequestSessions_AzureSubscription
    on dbo.ProviderRequestSessions (AzureTopicName, AzureSubscriptionName);

    create table dbo.ProviderRequestSessionTopics
    (
        ProviderRequestSessionTopicId uniqueidentifier not null
            constraint DF_ProviderRequestSessionTopics_Id default newid(),
        ProviderRequestSessionId uniqueidentifier not null,
        TopicOrdinal int not null,
        TopicValue nvarchar(512) collate Latin1_General_100_BIN2 not null,
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

    create unique index UQ_ProviderRequestSessionTopics_Session_Ordinal
    on dbo.ProviderRequestSessionTopics (ProviderRequestSessionId, TopicOrdinal);

    create unique index UQ_ProviderRequestSessionTopics_Session_TopicValue
    on dbo.ProviderRequestSessionTopics (ProviderRequestSessionId, TopicValue);

    create index IX_ProviderRequestSessionTopics_TopicValue
    on dbo.ProviderRequestSessionTopics (TopicValue)
    include (ProviderRequestSessionId);

    create table dbo.ConsumerRequestSessions
    (
        SessionId uniqueidentifier not null,
        ChannelId uniqueidentifier not null,
        SessionState nvarchar(32) not null,
        AuthenticatedTokenUsername nvarchar(256) null,
        ListenerUrl nvarchar(2048) null,
        CreatedUtc datetime2(7) not null
            constraint DF_ConsumerRequestSessions_CreatedUtc default sysutcdatetime(),
        ClosedUtc datetime2(7) null,
        RowVersion rowversion not null,

        constraint PK_ConsumerRequestSessions
            primary key clustered (SessionId),

        constraint FK_ConsumerRequestSessions_Channels
            foreign key (ChannelId)
            references dbo.Channels (ChannelId),

        constraint CK_ConsumerRequestSessions_SessionState
            check (SessionState in (N''Open'', N''Closed'')),

        constraint CK_ConsumerRequestSessions_ClosedAfterCreated
            check (ClosedUtc is null or ClosedUtc >= CreatedUtc)
    );

    create table dbo.ConsumerRequests
    (
        MessageId uniqueidentifier not null,
        ConsumerSessionId uniqueidentifier not null,
        ChannelId uniqueidentifier not null,
        CreatedUtc datetime2(7) not null,
        ExpiredUtc datetime2(7) null,
        EffectiveExpiryUtc datetime2(7) null,
        MessageContent nvarchar(max) null,
        MediaType nvarchar(256) null,
        Payload varbinary(max) null,
        PayloadKind nvarchar(16) null,
        ContentEncoding nvarchar(64) null,
        RowVersion rowversion not null,

        constraint PK_ConsumerRequests
            primary key clustered (MessageId),

        constraint FK_ConsumerRequests_ConsumerRequestSessions
            foreign key (ConsumerSessionId)
            references dbo.ConsumerRequestSessions (SessionId),

        constraint FK_ConsumerRequests_Channels
            foreign key (ChannelId)
            references dbo.Channels (ChannelId),

        constraint CK_ConsumerRequests_MessageContentPayloadKind
            check (PayloadKind is null or PayloadKind in (N''Json'', N''String'', N''Binary'')),

        constraint CK_ConsumerRequests_MessageContentPayloadShape
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
            )
    );

    create index IX_ConsumerRequests_Session_Message
    on dbo.ConsumerRequests (ConsumerSessionId, MessageId)
    include (ChannelId, ExpiredUtc);

    create table dbo.ConsumerRequestTopics
    (
        MessageId uniqueidentifier not null,
        TopicOrdinal int not null,
        TopicValue nvarchar(512) collate Latin1_General_100_BIN2 not null,

        constraint PK_ConsumerRequestTopics
            primary key clustered (MessageId, TopicOrdinal),

        constraint FK_ConsumerRequestTopics_Requests
            foreign key (MessageId)
            references dbo.ConsumerRequests (MessageId)
    );

    create unique index UQ_ConsumerRequestTopics_Message_TopicValue
    on dbo.ConsumerRequestTopics (MessageId, TopicValue);

    create table dbo.ConsumerRequestRecipients
    (
        RequestMessageId uniqueidentifier not null,
        ProviderRequestSessionId uniqueidentifier not null,
        State nvarchar(32) not null,
        TopicValue nvarchar(512) not null,
        ReadUtc datetime2(7) null,
        ResolvedUtc datetime2(7) null,
        CreatedUtc datetime2(7) not null
            constraint DF_ConsumerRequestRecipients_CreatedUtc default sysutcdatetime(),
        RowVersion rowversion not null,

        constraint PK_ConsumerRequestRecipients
            primary key clustered (RequestMessageId, ProviderRequestSessionId),

        constraint FK_ConsumerRequestRecipients_Requests
            foreign key (RequestMessageId)
            references dbo.ConsumerRequests (MessageId),

        constraint FK_ConsumerRequestRecipients_ProviderRequestSessions
            foreign key (ProviderRequestSessionId)
            references dbo.ProviderRequestSessions (SessionId),

        constraint CK_ConsumerRequestRecipients_State
            check (State in (N''Outstanding'', N''Removed''))
    );

    create unique index UQ_ConsumerRequestRecipients_Provider_Outstanding
    on dbo.ConsumerRequestRecipients (ProviderRequestSessionId)
    include (RequestMessageId, TopicValue, ReadUtc)
    where State = N''Outstanding'';

    create index IX_ConsumerRequestRecipients_Request_State_Provider
    on dbo.ConsumerRequestRecipients (RequestMessageId, State, ProviderRequestSessionId);

    create index IX_ConsumerRequestRecipients_Provider_State
    on dbo.ConsumerRequestRecipients (ProviderRequestSessionId, State, ResolvedUtc)
    include (RequestMessageId, TopicValue);

    create table dbo.ProviderPublicationSessions
    (
        SessionId uniqueidentifier not null,
        ChannelId uniqueidentifier not null,
        SessionState nvarchar(32) not null,
        AuthenticatedTokenUsername nvarchar(256) null,
        CreatedUtc datetime2(7) not null
            constraint DF_ProviderPublicationSessions_CreatedUtc default sysutcdatetime(),
        ClosedUtc datetime2(7) null,
        RowVersion rowversion not null,

        constraint PK_ProviderPublicationSessions
            primary key clustered (SessionId),

        constraint FK_ProviderPublicationSessions_Channels
            foreign key (ChannelId)
            references dbo.Channels (ChannelId),

        constraint CK_ProviderPublicationSessions_SessionState
            check (SessionState in (N''Open'', N''Closed'')),

        constraint CK_ProviderPublicationSessions_ClosedAfterCreated
            check (ClosedUtc is null or ClosedUtc >= CreatedUtc)
    );

    create index IX_ProviderPublicationSessions_ChannelId_Open
    on dbo.ProviderPublicationSessions (ChannelId, SessionState)
    include (CreatedUtc)
    where SessionState = N''Open'';

    create table dbo.ProviderPublications
    (
        MessageId uniqueidentifier not null,
        ProviderSessionId uniqueidentifier not null,
        ChannelId uniqueidentifier not null,
        PublishedUtc datetime2(7) not null,
        ExpiredUtc datetime2(7) null,
        EffectiveExpiryUtc datetime2(7) null,
        MessageContent nvarchar(max) null,
        MediaType nvarchar(256) null,
        Payload varbinary(max) null,
        PayloadKind nvarchar(16) null,
        ContentEncoding nvarchar(64) null,
        CreatedUtc datetime2(7) not null
            constraint DF_ProviderPublications_CreatedUtc default sysutcdatetime(),

        constraint PK_ProviderPublications
            primary key clustered (MessageId),

        constraint FK_ProviderPublications_ProviderPublicationSessions
            foreign key (ProviderSessionId)
            references dbo.ProviderPublicationSessions (SessionId),

        constraint FK_ProviderPublications_Channels
            foreign key (ChannelId)
            references dbo.Channels (ChannelId),

        constraint CK_ProviderPublications_MessageContentPayloadKind
            check (PayloadKind is null or PayloadKind in (N''Json'', N''String'', N''Binary'')),

        constraint CK_ProviderPublications_MessageContentPayloadShape
            check
            (
                (
                    MessageContent is null
                    and MediaType is null
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
            )
    );

    create index IX_ProviderPublications_ChannelId_MessageId
    on dbo.ProviderPublications (ChannelId, MessageId)
    include (ExpiredUtc);

    create index IX_ProviderPublications_ExpiredUtc
    on dbo.ProviderPublications (ExpiredUtc)
    where ExpiredUtc is not null;

    create table dbo.ProviderPublicationTopics
    (
        MessageId uniqueidentifier not null,
        TopicOrdinal int not null,
        TopicValue nvarchar(512) collate Latin1_General_100_BIN2 not null,

        constraint PK_ProviderPublicationTopics
            primary key clustered (MessageId, TopicOrdinal),

        constraint FK_ProviderPublicationTopics_Publications
            foreign key (MessageId)
            references dbo.ProviderPublications (MessageId)
    );

    create unique index UQ_ProviderPublicationTopics_Message_TopicValue
    on dbo.ProviderPublicationTopics (MessageId, TopicValue);

    create table dbo.ProviderPublicationRecipients
    (
        MessageId uniqueidentifier not null,
        ConsumerSessionId uniqueidentifier not null,
        AzureTopicName nvarchar(260) not null,
        AzureSubscriptionName nvarchar(260) not null,
        State nvarchar(32) not null,
        CreatedUtc datetime2(7) not null
            constraint DF_ProviderPublicationRecipients_CreatedUtc default sysutcdatetime(),
        ResolvedUtc datetime2(7) null,

        constraint PK_ProviderPublicationRecipients
            primary key clustered (MessageId, ConsumerSessionId),

        constraint FK_ProviderPublicationRecipients_Publications
            foreign key (MessageId)
            references dbo.ProviderPublications (MessageId),

        constraint FK_ProviderPublicationRecipients_ConsumerSessions
            foreign key (ConsumerSessionId)
            references dbo.ConsumerSubscriptionSessions (SessionId),

        constraint CK_ProviderPublicationRecipients_State
            check (State in (N''Pending'', N''Outstanding'', N''SkippedExpired'', N''Removed'', N''SubscriptionDeleted''))
    );

    create index IX_ProviderPublicationRecipients_Unresolved
    on dbo.ProviderPublicationRecipients (MessageId, State, ConsumerSessionId)
    include (AzureTopicName, AzureSubscriptionName)
    where State in (N''Pending'', N''Outstanding'');

    create index IX_ProviderPublicationRecipients_Consumer_Outstanding
    on dbo.ProviderPublicationRecipients (ConsumerSessionId, CreatedUtc, MessageId)
    include (AzureTopicName, AzureSubscriptionName, ResolvedUtc)
    where State = N''Outstanding'';

    create table dbo.Notifications
    (
        NotificationId uniqueidentifier not null
            constraint DF_Notifications_Id default newid(),
        NotificationServiceType nvarchar(32) not null
            constraint DF_Notifications_ServiceType default N''Publication'',
        MessageId uniqueidentifier not null,
        RequestMessageId uniqueidentifier null,
        ConsumerSubscriptionSessionId uniqueidentifier null,
        ProviderRequestSessionId uniqueidentifier null,
        ConsumerRequestSessionId uniqueidentifier null,
        ListenerUrl nvarchar(2048) not null,
        State nvarchar(32) not null,
        AttemptCount int not null
            constraint DF_Notifications_AttemptCount default 0,
        NextAttemptUtc datetime2(7) null
            constraint DF_Notifications_NextAttemptUtc default sysutcdatetime(),
        LastAttemptUtc datetime2(7) null,
        LastStatusCode int null,
        LastError nvarchar(2048) null,
        LockedUntilUtc datetime2(7) null,
        LockOwner nvarchar(128) null,
        ResolutionReason nvarchar(64) null,
        CreatedUtc datetime2(7) not null
            constraint DF_Notifications_CreatedUtc default sysutcdatetime(),
        DeliveryDeadlineUtc datetime2(7) not null,
        ResolvedUtc datetime2(7) null,
        RowVersion rowversion not null,

        constraint PK_Notifications
            primary key clustered (NotificationId),

        constraint FK_Notifications_ConsumerSubscriptionSessions
            foreign key (ConsumerSubscriptionSessionId)
            references dbo.ConsumerSubscriptionSessions (SessionId),

        constraint FK_Notifications_PublicationRecipients
            foreign key (MessageId, ConsumerSubscriptionSessionId)
            references dbo.ProviderPublicationRecipients (MessageId, ConsumerSessionId),

        constraint FK_Notifications_ProviderRequestSessions
            foreign key (ProviderRequestSessionId)
            references dbo.ProviderRequestSessions (SessionId),

        constraint FK_Notifications_ConsumerRequestSessions
            foreign key (ConsumerRequestSessionId)
            references dbo.ConsumerRequestSessions (SessionId),

        constraint FK_Notifications_RequestMessage_ConsumerRequests
            foreign key (RequestMessageId)
            references dbo.ConsumerRequests (MessageId),

        constraint CK_Notifications_ServiceType
            check (NotificationServiceType in (N''Publication'', N''Request'', N''Response'')),

        constraint CK_Notifications_RecipientShape
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
            ),

        constraint CK_Notifications_State
            check (State in (N''Pending'', N''Sending'', N''Delivered'', N''RetryScheduled'', N''Failed'', N''Suppressed'', N''Expired'')),

        constraint CK_Notifications_AttemptCount
            check (AttemptCount >= 0)
    );

    create unique index UQ_Notifications_Message_ConsumerSubscriptionSession_Publication
    on dbo.Notifications (MessageId, ConsumerSubscriptionSessionId)
    where NotificationServiceType = N''Publication'';

    create unique index UQ_Notifications_Message_ProviderRequestSession_Request
    on dbo.Notifications (MessageId, ProviderRequestSessionId)
    where NotificationServiceType = N''Request'';

    create unique index UQ_Notifications_Message_ConsumerRequestSession_Response
    on dbo.Notifications (MessageId, ConsumerRequestSessionId)
    where NotificationServiceType = N''Response'';

    create index IX_Notifications_Pending
    on dbo.Notifications (NotificationServiceType, State, NextAttemptUtc, LockedUntilUtc, DeliveryDeadlineUtc, CreatedUtc, NotificationId)
    include (MessageId, RequestMessageId, ConsumerSubscriptionSessionId, ProviderRequestSessionId, ConsumerRequestSessionId, ListenerUrl, AttemptCount, LockOwner)
    where State in (N''Pending'', N''RetryScheduled'', N''Sending'');

    create index IX_Notifications_ProviderRequestSession_State
    on dbo.Notifications (NotificationServiceType, ProviderRequestSessionId, State)
    include (MessageId, DeliveryDeadlineUtc)
    where NotificationServiceType = N''Request'';

    create index IX_Notifications_Response_ConsumerRequestSession_Pending
    on dbo.Notifications (ConsumerRequestSessionId, State)
    where NotificationServiceType = N''Response''
      and State in (N''Pending'', N''RetryScheduled'');

    create index IX_Notifications_Response_RequestMessage_Pending
    on dbo.Notifications (RequestMessageId, State)
    where NotificationServiceType = N''Response''
      and State in (N''Pending'', N''RetryScheduled'');

    create index IX_Notifications_Request_Message_Pending
    on dbo.Notifications (MessageId, State)
    where NotificationServiceType = N''Request''
      and State in (N''Pending'', N''RetryScheduled'');

    create table dbo.NotificationTopics
    (
        NotificationId uniqueidentifier not null,
        TopicOrdinal int not null,
        TopicValue nvarchar(512) collate Latin1_General_100_BIN2 not null,

        constraint PK_NotificationTopics
            primary key clustered (NotificationId, TopicOrdinal),

        constraint FK_NotificationTopics_Notifications
            foreign key (NotificationId)
            references dbo.Notifications (NotificationId)
    );

    create unique index UQ_NotificationTopics_Notification_TopicValue
    on dbo.NotificationTopics (NotificationId, TopicValue);

    create table dbo.RequestResponses
    (
        RequestResponseId bigint identity(1,1) not null,
        MessageId uniqueidentifier not null,
        RequestMessageId uniqueidentifier not null,
        ConsumerRequestSessionId uniqueidentifier not null,
        ProviderRequestSessionId uniqueidentifier not null,
        ChannelId uniqueidentifier not null,
        MessageContent nvarchar(max) not null,
        MediaType nvarchar(256) null,
        Payload varbinary(max) null,
        PayloadKind nvarchar(16) null,
        ContentEncoding nvarchar(64) null,
        CreatedUtc datetime2(7) not null,
        RemovedUtc datetime2(7) null,
        RowVersion rowversion not null,

        constraint PK_RequestResponses
            primary key clustered (RequestResponseId),

        constraint FK_RequestResponses_Requests
            foreign key (RequestMessageId)
            references dbo.ConsumerRequests (MessageId),

        constraint FK_RequestResponses_ConsumerRequestSessions
            foreign key (ConsumerRequestSessionId)
            references dbo.ConsumerRequestSessions (SessionId),

        constraint FK_RequestResponses_ProviderRequestSessions
            foreign key (ProviderRequestSessionId)
            references dbo.ProviderRequestSessions (SessionId),

        constraint FK_RequestResponses_Channels
            foreign key (ChannelId)
            references dbo.Channels (ChannelId),

        constraint CK_RequestResponses_MessageContentPayloadKind
            check (PayloadKind is null or PayloadKind in (N''Json'', N''String'', N''Binary'')),

        constraint CK_RequestResponses_MessageContentPayloadShape
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
            )
    );

    create unique index UQ_RequestResponses_MessageId
    on dbo.RequestResponses (MessageId);

    create index IX_RequestResponses_Session_Request_Removed_Sequence
    on dbo.RequestResponses (ConsumerRequestSessionId, RequestMessageId, RemovedUtc, RequestResponseId)
    include (MessageId, ChannelId, MediaType);

    create table dbo.OutstandingRequestResponses
    (
        ConsumerRequestSessionId uniqueidentifier not null,
        RequestMessageId uniqueidentifier not null,
        RequestResponseId bigint not null,
        ReadUtc datetime2(7) not null,
        RowVersion rowversion not null,

        constraint PK_OutstandingRequestResponses
            primary key clustered (ConsumerRequestSessionId, RequestMessageId),

        constraint FK_OutstandingRequestResponses_ConsumerRequestSessions
            foreign key (ConsumerRequestSessionId)
            references dbo.ConsumerRequestSessions (SessionId),

        constraint FK_OutstandingRequestResponses_Requests
            foreign key (RequestMessageId)
            references dbo.ConsumerRequests (MessageId),

        constraint FK_OutstandingRequestResponses_Responses
            foreign key (RequestResponseId)
            references dbo.RequestResponses (RequestResponseId)
    );

    create unique index UQ_OutstandingRequestResponses_Response
    on dbo.OutstandingRequestResponses (RequestResponseId);

    insert dbo.SchemaVersions (VersionNumber, Description, ScriptName)
    values
    (
        N''PhaseOne-ChannelPersistence'',
        N''Phase-one ISBM Channel and UsernameToken persistence schema.'',
        N''001_CreateDatabase.sql''
    ),
    (
        N''ChannelAzureTopicBinding-001'',
        N''Stable Azure Service Bus topic names stored on channels.'',
        N''001_CreateDatabase.sql''
    ),
    (
        N''ConsumerSubscriptionSessions-001'',
        N''Consumer publication subscription-session persistence for Azure Service Bus subscriptions.'',
        N''001_CreateDatabase.sql''
    ),
    (
        N''ConsumerSubscriptionSessionTopics-001'',
        N''Persisted ISBM topic selections for consumer publication subscription sessions.'',
        N''001_CreateDatabase.sql''
    ),
    (
        N''ProviderPublicationSessions-001'',
        N''Provider publication-session persistence.'',
        N''001_CreateDatabase.sql''
    ),
    (
        N''ProviderPublicationExpiration-001'',
        N''Adds provider publication metadata, logical expiration, and per-consumer recipient state.'',
        N''001_CreateDatabase.sql''
    ),
    (
        N''ConsumerPublicationListenerUrl-001'',
        N''Optional listener URL for consumer publication subscription sessions.'',
        N''001_CreateDatabase.sql''
    ),
    (
        N''RequestServices-001'',
        N''Adds provider and consumer request-service sessions and request expiration ownership metadata.'',
        N''001_CreateDatabase.sql''
    ),
    (
        N''RequestResponses-001'',
        N''Adds request-response messages and outstanding response-read persistence.'',
        N''001_CreateDatabase.sql''
    ),
    (
        N''RequestResponseMessageContentPayload-001'',
        N''Adds media-neutral MessageContent payload fields for request-response persistence.'',
        N''001_CreateDatabase.sql''
    ),
    (
        N''PublicationNotifications-001'',
        N''Durable pending publication notification work for listener-enabled consumer publication sessions.'',
        N''001_CreateDatabase.sql''
    ),
    (
        N''PublicationNotificationDispatch-001'',
        N''Dispatch lease, retry, and terminal result fields for publication notifications.'',
        N''001_CreateDatabase.sql''
    ),
    (
        N''PublicationNotifications-002'',
        N''Delivery deadline and expired terminal state for publication notifications.'',
        N''001_CreateDatabase.sql''
    ),
    (
        N''RequestNotifications-001'',
        N''Adds notification service type and request-recipient shape support for shared notification work.'',
        N''001_CreateDatabase.sql''
    ),
    (
        N''SessionNotificationModel-001'',
        N''Refactors provider request sessions into dedicated tables and generalizes notification table names.'',
        N''001_CreateDatabase.sql''
    ),
    (
        N''ResponseNotifications-001'',
        N''Adds response notification schema shape and request-message correlation.'',
        N''001_CreateDatabase.sql''
    ),
    (
        N''SessionFilterExpressions-001'',
        N''Adds nullable JSON storage for accepted session filterExpressions.'',
        N''001_CreateDatabase.sql''
    ),
    (
        N''TopicValueSemantics-001'',
        N''Aligns topic equality with binary code-point semantics and prevents duplicate consumer request topics.'',
        N''001_CreateDatabase.sql''
    ),
    (
        N''RequestRecipientState-001'',
        N''Adds durable request content and provider request recipient state.'',
        N''001_CreateDatabase.sql''
    ),
    (
        N''PublicationMessageContent-001'',
        N''Adds durable publication message content for restart-safe publication lifecycle.'',
        N''001_CreateDatabase.sql''
    ),
    (
        N''NotificationCleanupIndexes-001'',
        N''Adds targeted indexes for notification cleanup and subscription reconciliation queries.'',
        N''001_CreateDatabase.sql''
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

';

exec sys.sp_executesql @InstallSchemaSql;
print 'RapidRedPanda ISBM Host schema is installed.';
go