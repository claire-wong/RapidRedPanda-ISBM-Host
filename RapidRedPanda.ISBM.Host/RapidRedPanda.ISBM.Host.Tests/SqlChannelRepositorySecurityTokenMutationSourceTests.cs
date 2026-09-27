namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class SqlChannelRepositorySecurityTokenMutationSourceTests
{
    [Fact]
    public async Task AddSecurityTokens_UsesChannelTokenTypeAndNormalizedUsernameAsAssociationIdentity()
    {
        string repository = await ReadRepositoryAsync();

        Assert.Contains("where ChannelId = @ChannelId", repository, StringComparison.Ordinal);
        Assert.Contains("and TokenType = @TokenType", repository, StringComparison.Ordinal);
        Assert.Contains("and NormalizedUsername = @NormalizedUsername", repository, StringComparison.Ordinal);
        Assert.Contains("where not exists", repository, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddSecurityTokens_DoesNotOverwriteExistingPasswordHashForDuplicateAssociation()
    {
        string repository = await ReadRepositoryAsync();

        Assert.Contains("insert dbo.ChannelSecurityTokens", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("update dbo.ChannelSecurityTokens\r\n                           set PasswordHash", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("update dbo.ChannelSecurityTokens\n                           set PasswordHash", repository, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoveSecurityTokens_DoesNotUsePasswordForRemovalIdentity()
    {
        string repository = await ReadRepositoryAsync();
        int removeMethodIndex = repository.IndexOf("public async Task<SecurityTokenMutationResult> RemoveSecurityTokensAsync", StringComparison.Ordinal);
        Assert.NotEqual(-1, removeMethodIndex);

        string removeMethod = repository[removeMethodIndex..];

        Assert.Contains("and TokenType = @TokenType", removeMethod, StringComparison.Ordinal);
        Assert.Contains("and NormalizedUsername = @NormalizedUsername", removeMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordHash", removeMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("BCrypt", removeMethod, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecurityTokenMutations_UseExplicitTransactions()
    {
        string repository = await ReadRepositoryAsync();

        Assert.Contains("AddSecurityTokensAsync", repository, StringComparison.Ordinal);
        Assert.Contains("RemoveSecurityTokensAsync", repository, StringComparison.Ordinal);
        Assert.True(CountOccurrences(repository, "set xact_abort on;") >= 3);
        Assert.True(CountOccurrences(repository, "BeginTransactionAsync") >= 4);
    }

    [Fact]
    public async Task GetChannel_ReturnsTokenMetadataWithoutPasswordHash()
    {
        string repository = await ReadRepositoryAsync();
        int getMethodIndex = repository.IndexOf("public async Task<GetChannelResult> GetChannelAsync", StringComparison.Ordinal);
        int createMethodIndex = repository.IndexOf("public async Task<CreateChannelResult> CreateChannelAsync", StringComparison.Ordinal);
        Assert.NotEqual(-1, getMethodIndex);
        Assert.NotEqual(-1, createMethodIndex);

        string getMethod = repository[getMethodIndex..createMethodIndex];

        Assert.Contains("st.TokenType", getMethod, StringComparison.Ordinal);
        Assert.Contains("st.Username", getMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordHash", getMethod, StringComparison.Ordinal);
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
}
