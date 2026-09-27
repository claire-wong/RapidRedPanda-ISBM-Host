namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class SqlProviderPublicationSessionRepositorySourceTests
{
    [Fact]
    public async Task RepositorySql_UsesParametersAndUtcLifecycleTimestamps()
    {
        string repository = await ReadRepositoryAsync();

        Assert.Contains("@SessionId", repository, StringComparison.Ordinal);
        Assert.Contains("@ChannelId", repository, StringComparison.Ordinal);
        Assert.Contains("sysutcdatetime()", repository, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("coalesce(ClosedUtc, sysutcdatetime())", repository, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("and ClosedUtc is null", repository, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("and SessionState = @OpenState", repository, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BeginTransactionAsync", repository, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepositorySource_DoesNotStoreSecretsOrAzureCredentials()
    {
        string repository = await ReadRepositoryAsync();

        Assert.DoesNotContain("Password", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SharedAccessKey", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AzureTopicName", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AzureSubscriptionName", repository, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadRepositoryAsync()
    {
        string path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "RapidRedPanda-ISBM-Host",
            "Data",
            "SqlProviderPublicationSessionRepository.cs"));

        return await File.ReadAllTextAsync(path);
    }

}
