namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class RequestResponseMessageContentSqlScriptTests
{
    private static readonly string ScriptsRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "Database",
        "Scripts"));

    [Fact]
    public void FreshInstall_CreatesMediaNeutralRequestResponsePayloadColumns()
    {
        string script = ReadScript("Install", "001_CreateDatabase.sql");

        Assert.Contains("create table dbo.RequestResponses", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MessageContent nvarchar(max) not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Payload varbinary(max) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PayloadKind nvarchar(16) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ContentEncoding nvarchar(64) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CK_RequestResponses_MessageContentPayloadKind", script, StringComparison.Ordinal);
        Assert.Contains("CK_RequestResponses_MessageContentPayloadShape", script, StringComparison.Ordinal);
        Assert.Contains("RequestResponseMessageContentPayload-001", script, StringComparison.Ordinal);
    }

    [Fact]
    public void FreshInstall_CreatesDurableConsumerRequestContentAndRecipientState()
    {
        string script = ReadScript("Install", "001_CreateDatabase.sql");

        Assert.Contains("create table dbo.ConsumerRequests", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create table dbo.ConsumerRequestRecipients", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MessageContent nvarchar(max) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Payload varbinary(max) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PayloadKind nvarchar(16) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ContentEncoding nvarchar(64) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CK_ConsumerRequests_MessageContentPayloadKind", script, StringComparison.Ordinal);
        Assert.Contains("CK_ConsumerRequests_MessageContentPayloadShape", script, StringComparison.Ordinal);
        Assert.Contains("RequestRecipientState-001", script, StringComparison.Ordinal);
        Assert.DoesNotContain("on delete cascade", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FreshInstall_CreatesDurableProviderPublicationContentAndRecipientIndexes()
    {
        string script = ReadScript("Install", "001_CreateDatabase.sql");

        Assert.Contains("create table dbo.ProviderPublications", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MessageContent nvarchar(max) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MediaType nvarchar(256) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Payload varbinary(max) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PayloadKind nvarchar(16) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ContentEncoding nvarchar(64) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CK_ProviderPublications_MessageContentPayloadKind", script, StringComparison.Ordinal);
        Assert.Contains("CK_ProviderPublications_MessageContentPayloadShape", script, StringComparison.Ordinal);
        Assert.Contains("IX_ProviderPublicationRecipients_Consumer_Outstanding", script, StringComparison.Ordinal);
        Assert.Contains("PublicationMessageContent-001", script, StringComparison.Ordinal);
    }

    private static string ReadScript(params string[] segments)
    {
        return File.ReadAllText(Path.Combine([ScriptsRoot, .. segments]));
    }
}