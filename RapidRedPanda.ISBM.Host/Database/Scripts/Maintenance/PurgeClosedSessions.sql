/*
    RapidRedPanda ISBM Host
    Development/test maintenance script.

    Deletes closed sessions older than a required cutoff and their dependent
    runtime rows. This is intended for developer/test cleanup, not production
    retention management.

    Before executing in SSMS or Azure Data Studio:
    1. Review the target database.
    2. Set @ConfirmPurge = N'YES'.
    3. Set @ClosedBeforeUtcText to an ISO cutoff, for example N'2026-01-01T00:00:00'.
    4. Execute the script normally. SQLCMD Mode is not required.
*/

set nocount on;
set xact_abort on;
set quoted_identifier on;

declare @ConfirmPurge nvarchar(3) = N'NO';
declare @ClosedBeforeUtcText nvarchar(33) = N'';

begin transaction;

begin try
    if @ConfirmPurge <> N'YES'
        throw 54030, 'Refusing to purge closed sessions. Set @ConfirmPurge = N''YES'' before executing.', 1;

    declare @ClosedBeforeUtc datetime2(7) = try_convert(datetime2(7), @ClosedBeforeUtcText, 126);

    if @ClosedBeforeUtc is null
        throw 54031, 'ClosedBeforeUtcText must be provided in ISO format, for example 2026-01-01T00:00:00.', 1;

    declare @ConsumerSubscriptionSessions table (SessionId uniqueidentifier not null primary key);
    declare @ProviderRequestSessions table (SessionId uniqueidentifier not null primary key);
    declare @ConsumerRequestSessions table (SessionId uniqueidentifier not null primary key);
    declare @ProviderPublicationSessions table (SessionId uniqueidentifier not null primary key);
    declare @ConsumerRequests table (MessageId uniqueidentifier not null primary key);
    declare @ProviderPublications table (MessageId uniqueidentifier not null primary key);
    declare @RequestResponses table (RequestResponseId bigint not null primary key, MessageId uniqueidentifier not null unique);
    declare @Notifications table (NotificationId uniqueidentifier not null primary key);

    insert @ConsumerSubscriptionSessions (SessionId)
    select SessionId
    from dbo.ConsumerSubscriptionSessions
    where SessionState = N'Closed'
      and ClosedUtc is not null
      and ClosedUtc < @ClosedBeforeUtc;

    insert @ProviderRequestSessions (SessionId)
    select SessionId
    from dbo.ProviderRequestSessions
    where SessionState = N'Closed'
      and ClosedUtc is not null
      and ClosedUtc < @ClosedBeforeUtc;

    insert @ConsumerRequestSessions (SessionId)
    select SessionId
    from dbo.ConsumerRequestSessions
    where SessionState = N'Closed'
      and ClosedUtc is not null
      and ClosedUtc < @ClosedBeforeUtc;

    insert @ProviderPublicationSessions (SessionId)
    select SessionId
    from dbo.ProviderPublicationSessions
    where SessionState = N'Closed'
      and ClosedUtc is not null
      and ClosedUtc < @ClosedBeforeUtc;

    insert @ConsumerRequests (MessageId)
    select request.MessageId
    from dbo.ConsumerRequests request
    inner join @ConsumerRequestSessions session
            on session.SessionId = request.ConsumerSessionId;

    insert @ProviderPublications (MessageId)
    select publication.MessageId
    from dbo.ProviderPublications publication
    inner join @ProviderPublicationSessions session
            on session.SessionId = publication.ProviderSessionId;

    insert @RequestResponses (RequestResponseId, MessageId)
    select response.RequestResponseId, response.MessageId
    from dbo.RequestResponses response
    where exists (select 1 from @ConsumerRequestSessions session where session.SessionId = response.ConsumerRequestSessionId)
       or exists (select 1 from @ProviderRequestSessions session where session.SessionId = response.ProviderRequestSessionId)
       or exists (select 1 from @ConsumerRequests request where request.MessageId = response.RequestMessageId);

    insert @Notifications (NotificationId)
    select notification.NotificationId
    from dbo.Notifications notification
    where exists (select 1 from @ConsumerSubscriptionSessions session where session.SessionId = notification.ConsumerSubscriptionSessionId)
       or exists (select 1 from @ProviderRequestSessions session where session.SessionId = notification.ProviderRequestSessionId)
       or exists (select 1 from @ConsumerRequestSessions session where session.SessionId = notification.ConsumerRequestSessionId)
       or exists (select 1 from @ConsumerRequests request where request.MessageId = notification.MessageId)
       or exists (select 1 from @ConsumerRequests request where request.MessageId = notification.RequestMessageId)
       or exists (select 1 from @ProviderPublications publication where publication.MessageId = notification.MessageId)
       or exists (select 1 from @RequestResponses response where response.MessageId = notification.MessageId);

    delete topic
    from dbo.NotificationTopics topic
    inner join @Notifications notification
            on notification.NotificationId = topic.NotificationId;

    delete notification
    from dbo.Notifications notification
    inner join @Notifications purge
            on purge.NotificationId = notification.NotificationId;

    delete outstanding
    from dbo.OutstandingRequestResponses outstanding
    where exists (select 1 from @RequestResponses response where response.RequestResponseId = outstanding.RequestResponseId)
       or exists (select 1 from @ConsumerRequestSessions session where session.SessionId = outstanding.ConsumerRequestSessionId)
       or exists (select 1 from @ConsumerRequests request where request.MessageId = outstanding.RequestMessageId);

    delete response
    from dbo.RequestResponses response
    inner join @RequestResponses purge
            on purge.RequestResponseId = response.RequestResponseId;

    delete recipient
    from dbo.ConsumerRequestRecipients recipient
    where exists (select 1 from @ProviderRequestSessions session where session.SessionId = recipient.ProviderRequestSessionId)
       or exists (select 1 from @ConsumerRequests request where request.MessageId = recipient.RequestMessageId);

    delete recipient
    from dbo.ProviderPublicationRecipients recipient
    where exists (select 1 from @ConsumerSubscriptionSessions session where session.SessionId = recipient.ConsumerSessionId)
       or exists (select 1 from @ProviderPublications publication where publication.MessageId = recipient.MessageId);

    delete topic
    from dbo.ProviderPublicationTopics topic
    inner join @ProviderPublications publication
            on publication.MessageId = topic.MessageId;

    delete publication
    from dbo.ProviderPublications publication
    inner join @ProviderPublications purge
            on purge.MessageId = publication.MessageId;

    delete topic
    from dbo.ConsumerRequestTopics topic
    inner join @ConsumerRequests request
            on request.MessageId = topic.MessageId;

    delete request
    from dbo.ConsumerRequests request
    inner join @ConsumerRequests purge
            on purge.MessageId = request.MessageId;

    delete topic
    from dbo.ProviderRequestSessionTopics topic
    inner join @ProviderRequestSessions session
            on session.SessionId = topic.ProviderRequestSessionId;

    delete session
    from dbo.ProviderRequestSessions session
    inner join @ProviderRequestSessions purge
            on purge.SessionId = session.SessionId;

    delete topic
    from dbo.ConsumerSubscriptionSessionTopics topic
    inner join @ConsumerSubscriptionSessions session
            on session.SessionId = topic.SessionId;

    delete session
    from dbo.ConsumerSubscriptionSessions session
    inner join @ConsumerSubscriptionSessions purge
            on purge.SessionId = session.SessionId;

    delete session
    from dbo.ConsumerRequestSessions session
    inner join @ConsumerRequestSessions purge
            on purge.SessionId = session.SessionId;

    delete session
    from dbo.ProviderPublicationSessions session
    inner join @ProviderPublicationSessions purge
            on purge.SessionId = session.SessionId;

    commit transaction;

    print 'Closed session purge completed.';
end try
begin catch
    if @@trancount > 0
        rollback transaction;

    throw;
end catch;
go
