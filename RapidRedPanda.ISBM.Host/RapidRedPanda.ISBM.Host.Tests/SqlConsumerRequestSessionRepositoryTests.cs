namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class SqlConsumerRequestSessionRepositoryTests
{
    [Fact]
    public async Task CloseConsumerRequestSessionSource_SuppressesResponseNotificationsInsideCloseTransaction()
    {
        string source = await ReadHostFileAsync("Data", "SqlConsumerRequestSessionRepository.cs");

        Assert.Contains("await connection.BeginTransactionAsync", source, StringComparison.Ordinal);
        Assert.Contains("set SessionState = @ClosedState", source, StringComparison.Ordinal);
        Assert.Contains("ClosedUtc = coalesce(ClosedUtc, @ClosedUtc)", source, StringComparison.Ordinal);
        Assert.Contains("SqlResponseNotificationSuppression.SuppressForConsumerRequestSessionAsync", source, StringComparison.Ordinal);
        Assert.Contains("PublicationNotificationSuppressionReasons.SessionClosed", source, StringComparison.Ordinal);
        Assert.Contains("await transaction.CommitAsync", source, StringComparison.Ordinal);
        Assert.Contains("await RollbackQuietlyAsync(transaction", source, StringComparison.Ordinal);
    }

    private static async Task<string> ReadHostFileAsync(params string[] pathSegments)
    {
        string path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "RapidRedPanda-ISBM-Host",
            Path.Combine(pathSegments)));

        return await File.ReadAllTextAsync(path);
    }
}
