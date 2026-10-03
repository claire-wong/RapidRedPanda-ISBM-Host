using Microsoft.Data.SqlClient;
using System.Data;

namespace RapidRedPanda.ISBM.Host.Data;

internal static class SqlResponseNotificationSuppression
{
    public static Task<int> SuppressForConsumerRequestSessionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid consumerRequestSessionId,
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
             where NotificationServiceType = @ResponseServiceType
               and ConsumerRequestSessionId = @ConsumerRequestSessionId
               and State in (@PendingState, @RetryScheduledState);
            """;

        return ExecuteAsync(
            connection,
            transaction,
            sql,
            command => command.Parameters.Add("@ConsumerRequestSessionId", SqlDbType.UniqueIdentifier).Value = consumerRequestSessionId,
            suppressedUtc,
            resolutionReason,
            cancellationToken);
    }

    public static Task<int> SuppressForRequestAsync(
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
             where NotificationServiceType = @ResponseServiceType
               and RequestMessageId = @RequestMessageId
               and State in (@PendingState, @RetryScheduledState);
            """;

        return ExecuteAsync(
            connection,
            transaction,
            sql,
            command => command.Parameters.Add("@RequestMessageId", SqlDbType.UniqueIdentifier).Value = requestMessageId,
            suppressedUtc,
            resolutionReason,
            cancellationToken);
    }

    public static Task<int> SuppressForResponseAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid responseMessageId,
        Guid consumerRequestSessionId,
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
             where NotificationServiceType = @ResponseServiceType
               and MessageId = @ResponseMessageId
               and ConsumerRequestSessionId = @ConsumerRequestSessionId
               and State in (@PendingState, @RetryScheduledState);
            """;

        return ExecuteAsync(
            connection,
            transaction,
            sql,
            command =>
            {
                command.Parameters.Add("@ResponseMessageId", SqlDbType.UniqueIdentifier).Value = responseMessageId;
                command.Parameters.Add("@ConsumerRequestSessionId", SqlDbType.UniqueIdentifier).Value = consumerRequestSessionId;
            },
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
        command.Parameters.Add("@ResponseServiceType", SqlDbType.NVarChar, 32).Value = NotificationServiceTypes.Response;
        command.Parameters.Add("@SuppressedState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.Suppressed;
        command.Parameters.Add("@PendingState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.Pending;
        command.Parameters.Add("@RetryScheduledState", SqlDbType.NVarChar, 32).Value = PublicationNotificationStates.RetryScheduled;
        command.Parameters.Add("@SuppressedUtc", SqlDbType.DateTime2).Value = suppressedUtc;
        command.Parameters.Add("@ResolutionReason", SqlDbType.NVarChar, 64).Value = resolutionReason;
        addScopeParameter(command);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
