namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class SqlChannelRepositoryDeleteSourceTests
{
    [Fact]
    public async Task DeleteChannel_SoftDeletesSecurityTokensBeforeChannel()
    {
        string repository = await ReadRepositoryAsync();

        int tokenUpdateIndex = repository.IndexOf("update dbo.ChannelSecurityTokens", StringComparison.Ordinal);
        int channelUpdateIndex = repository.IndexOf("update dbo.Channels", StringComparison.Ordinal);

        Assert.NotEqual(-1, tokenUpdateIndex);
        Assert.NotEqual(-1, channelUpdateIndex);
        Assert.True(tokenUpdateIndex < channelUpdateIndex);
    }

    [Fact]
    public async Task DeleteChannel_UsesOneServerUtcTimestampForTokenAndChannelSoftDelete()
    {
        string repository = await ReadRepositoryAsync();
        string deleteChannelMethod = ExtractMethod(
            repository,
            "public async Task<DeleteChannelResult> DeleteChannelAsync",
            "public async Task<SecurityTokenMutationResult> AddSecurityTokensAsync");

        Assert.Equal(1, CountOccurrences(deleteChannelMethod, "select sysutcdatetime();"));
        Assert.Equal(2, CountOccurrences(deleteChannelMethod, "command.Parameters.Add(\"@DeletedUtc\", SqlDbType.DateTime2).Value = deletedUtc;"));
    }

    [Fact]
    public async Task DeleteChannel_UsesXactAbortAndSingleExplicitTransaction()
    {
        string repository = await ReadRepositoryAsync();

        Assert.Contains("set xact_abort on;", repository, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BeginTransactionAsync", repository, StringComparison.Ordinal);
        Assert.Contains("CommitAsync", repository, StringComparison.Ordinal);
        Assert.Contains("RollbackQuietlyAsync", repository, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteChannel_LooksUpActiveChannelByCaseSensitiveChannelUri()
    {
        string repository = await ReadRepositoryAsync();

        Assert.Contains("ChannelUri collate Latin1_General_100_CS_AS_SC = @ChannelUri collate Latin1_General_100_CS_AS_SC", repository, StringComparison.Ordinal);
        Assert.Contains("and DeletedUtc is null", repository, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteChannel_DoesNotPhysicallyDeleteChannelOrTokenRows()
    {
        string repository = await ReadRepositoryAsync();

        Assert.DoesNotContain("delete dbo.Channels", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("delete from dbo.Channels", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("delete dbo.ChannelSecurityTokens", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("delete from dbo.ChannelSecurityTokens", repository, StringComparison.OrdinalIgnoreCase);
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
            "SqlChannelRepository.cs"));

        return await File.ReadAllTextAsync(path);
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int startIndex = 0;

        while (true)
        {
            int index = text.IndexOf(value, startIndex, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            startIndex = index + value.Length;
        }
    }

    private static string ExtractMethod(string source, string methodStart, string nextMethodStart)
    {
        int startIndex = source.IndexOf(methodStart, StringComparison.Ordinal);
        Assert.NotEqual(-1, startIndex);

        int endIndex = source.IndexOf(nextMethodStart, startIndex, StringComparison.Ordinal);
        Assert.NotEqual(-1, endIndex);

        return source[startIndex..endIndex];
    }
}
