/*
    RapidRedPanda ISBM Host
    Development/test maintenance script.

    Deletes terminal notification rows older than a required cutoff.
    Pending, retry-scheduled, and sending notifications are never selected.

    Before executing in SSMS or Azure Data Studio:
    1. Review the target database.
    2. Set @ConfirmPurge = N'YES'.
    3. Set @ResolvedBeforeUtcText to an ISO cutoff, for example N'2026-01-01T00:00:00'.
    4. Execute the script normally. SQLCMD Mode is not required.
*/

set nocount on;
set xact_abort on;
set quoted_identifier on;

declare @ConfirmPurge nvarchar(3) = N'NO';
declare @ResolvedBeforeUtcText nvarchar(33) = N'';

begin transaction;

begin try
    if @ConfirmPurge <> N'YES'
        throw 54020, 'Refusing to purge notifications. Set @ConfirmPurge = N''YES'' before executing.', 1;

    declare @ResolvedBeforeUtc datetime2(7) = try_convert(datetime2(7), @ResolvedBeforeUtcText, 126);

    if @ResolvedBeforeUtc is null
        throw 54021, 'ResolvedBeforeUtcText must be provided in ISO format, for example 2026-01-01T00:00:00.', 1;

    declare @NotificationIds table
    (
        NotificationId uniqueidentifier not null primary key
    );

    insert @NotificationIds (NotificationId)
    select NotificationId
    from dbo.Notifications
    where State in (N'Delivered', N'Failed', N'Suppressed', N'Expired')
      and ResolvedUtc is not null
      and ResolvedUtc < @ResolvedBeforeUtc;

    delete topic
    from dbo.NotificationTopics topic
    inner join @NotificationIds notification
            on notification.NotificationId = topic.NotificationId;

    delete notification
    from dbo.Notifications notification
    inner join @NotificationIds purge
            on purge.NotificationId = notification.NotificationId;

    declare @PurgedCount int = @@rowcount;

    commit transaction;

    print concat('Purged resolved notifications: ', @PurgedCount);
end try
begin catch
    if @@trancount > 0
        rollback transaction;

    throw;
end catch;
go
