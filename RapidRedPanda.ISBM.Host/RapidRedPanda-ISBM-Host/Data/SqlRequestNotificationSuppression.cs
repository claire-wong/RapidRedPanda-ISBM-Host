using Microsoft.Data.SqlClient;
using System.Data;

namespace RapidRedPanda.ISBM.Host.Data;

internal static class SqlRequestNotificationSuppression
{
    public static Task<int> SuppressForSessionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid providerRequestSessionId,
        DateTime suppressedUtc,
        string resolutionReason,
        CancellationToken cancellationToken)
    {
        const string sql = """
            update dbo.Notifications
               set State = @SuppressedState,
                   ResolvedUtc = @SuppressedUtc,
                   ResolutionReason = @ResolutionReason,
                   LastError = null,
                   NextAttemptUtc = null,
                   LockOwner = null,
                   LockedUntilUtc = null
             where NotificationServiceType = @RequestServiceType
               and ProviderRequestSessionId = @ProviderRequestSessionId
               and State in (@PendingState, @RetryScheduledState);
            """;

        return ExecuteAsync(
            connection,
            transaction,
            sql,
            command => command.Parameters.Add("@ProviderRequestSessionId", SqlDbType.UniqueIdentifier).Value = providerRequestSessionId,
            suppressedUtc,
            resolutionReason,
            cancellationToken);
    }

    public static Task<int> SuppressForMessageAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid requestMessageId,
        DateTime suppressedUtc,
        string resolutionReason,
        CancellationToken cancellationToken)
    {
        const string sql = """
            update dbo.Notifications
               set State = @SuppressedState,
                   ResolvedUtc = @SuppressedUtc,
                   ResolutionReason = @ResolutionReason,
                   LastError = null,
                   NextAttemptUtc = null,
                   LockOwner = null,
                   LockedUntilUtc = null
             where NotificationServiceType = @RequestServiceType
               and MessageId = @MessageId
               and State in (@PendingState, @RetryScheduledState);
            """;

        return ExecuteAsync(
            connection,
            transaction,
            sql,
            command => command.Parameters.Add("@MessageId", SqlDbType.UniqueIdentifier).Value = requestMessageId,
            suppressedUtc,
            resolutionReason,
            cancellationToken);
    }

    private static async Task<int> ExecuteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        Action<SqlCommand> addScopeParameter,
        DateTime suppressedUtc,
        string resolutionReason,
        CancellationToken cancellationToken)
    {
        await using SqlCommand command = new(sql, connection, transaction);
        command.Parameters.Add("@RequestServiceType", SqlDbType.NVarChar, 32).Value = NotificationServiceTypes.Request;
        command.Parameters.Add("@SuppressedState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.Suppressed;
        command.Parameters.Add("@PendingState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.Pending;
        command.Parameters.Add("@RetryScheduledState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.RetryScheduled;
        command.Parameters.Add("@SuppressedUtc", SqlDbType.DateTime2).Value = suppressedUtc;
        command.Parameters.Add("@ResolutionReason", SqlDbType.NVarChar, 64).Value = resolutionReason;
        addScopeParameter(command);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
