/*
    RapidRedPanda ISBM Host
    Development/test maintenance script.

    Deletes runtime/session/message/notification data while preserving:
    - dbo.SchemaVersions
    - dbo.Channels
    - dbo.ChannelSecurityTokens

    Before executing in SSMS or Azure Data Studio:
    1. Review the target database.
    2. Set @ConfirmRuntimeReset = N'YES'.
    3. Execute the script normally. SQLCMD Mode is not required.
*/

set nocount on;
set xact_abort on;
set quoted_identifier on;

declare @ConfirmRuntimeReset nvarchar(3) = N'NO';

begin transaction;

begin try
    if @ConfirmRuntimeReset <> N'YES'
        throw 54010, 'Refusing to reset runtime data. Set @ConfirmRuntimeReset = N''YES'' before executing.', 1;

    delete from dbo.NotificationTopics;
    delete from dbo.Notifications;

    delete from dbo.OutstandingRequestResponses;
    delete from dbo.RequestResponses;

    delete from dbo.ConsumerRequestRecipients;
    delete from dbo.ProviderPublicationRecipients;

    delete from dbo.ProviderPublicationTopics;
    delete from dbo.ProviderPublications;

    delete from dbo.ConsumerRequestTopics;
    delete from dbo.ConsumerRequests;

    delete from dbo.ProviderRequestSessionTopics;
    delete from dbo.ProviderRequestSessions;

    delete from dbo.ConsumerSubscriptionSessionTopics;
    delete from dbo.ConsumerSubscriptionSessions;

    delete from dbo.ConsumerRequestSessions;
    delete from dbo.ProviderPublicationSessions;

    if object_id(N'dbo.RequestResponses', N'U') is not null
        dbcc checkident (N'dbo.RequestResponses', reseed, 0) with no_infomsgs;

    commit transaction;

    print 'Runtime data was reset. Channels, ChannelSecurityTokens, and SchemaVersions were preserved.';
end try
begin catch
    if @@trancount > 0
        rollback transaction;

    throw;
end catch;
go
