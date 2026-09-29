namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class PhaseTwoLifecycleComplianceTests
{
    [Fact]
    public async Task PublicationClose_ExpiresOwnedUnexpiredMessagesInSameTransaction()
    {
        string source = await ReadHostSourceAsync("Data", "SqlProviderPublicationSessionRepository.cs");

        Assert.Contains("BeginTransactionAsync", source, StringComparison.Ordinal);
        Assert.Contains("update dbo.ProviderPublicationSessions", source, StringComparison.Ordinal);
        Assert.Contains("update dbo.ProviderPublications", source, StringComparison.Ordinal);
        Assert.Contains("ProviderSessionId = @SessionId", source, StringComparison.Ordinal);
        Assert.Contains("ExpiredUtc is null", source, StringComparison.Ordinal);
        Assert.Contains("transaction.CommitAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConsumerRequestClose_ExpiresOwnedUnexpiredRequestsInSameTransaction()
    {
        string source = await ReadHostSourceAsync("Data", "SqlConsumerRequestSessionRepository.cs");

        Assert.Contains("BeginTransactionAsync", source, StringComparison.Ordinal);
        Assert.Contains("update dbo.ConsumerRequestSessions", source, StringComparison.Ordinal);
        Assert.Contains("update dbo.ConsumerRequests", source, StringComparison.Ordinal);
        Assert.Contains("ConsumerSessionId = @SessionId", source, StringComparison.Ordinal);
        Assert.Contains("ExpiredUtc is null", source, StringComparison.Ordinal);
        Assert.Contains("transaction.CommitAsync", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SqlSubscriptionSessionRepository.cs")]
    [InlineData("SqlProviderRequestSessionRepository.cs")]
    public async Task ReceivingSessionClose_DoesNotExpireMessages(string file)
    {
        string source = await ReadHostSourceAsync("Data", file);

        Assert.DoesNotContain("set ExpiredUtc", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChannelTokenMutations_EnforceOpenChannelRuleAndAtomicPrevalidation()
    {
        string source = await ReadHostSourceAsync("Data", "SqlChannelRepository.cs");

        Assert.Contains("SecurityTokenMutationFailureKind.OpenChannel", source, StringComparison.Ordinal);
        Assert.Contains("SecurityTokenMutationFailureKind.SecurityTokenNotFound", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("SecurityTokenNotFound", StringComparison.Ordinal) <
            source.LastIndexOf("set DeletedUtc = @DeletedUtc", StringComparison.Ordinal));
    }

    private static async Task<string> ReadHostSourceAsync(params string[] parts)
    {
        string[] path = [
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "RapidRedPanda-ISBM-Host",
            .. parts
        ];
        return await File.ReadAllTextAsync(Path.GetFullPath(Path.Combine(path)));
    }
}
