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
    public void Migration_AddsMediaNeutralRequestResponsePayloadColumns()
    {
        string script = ReadScript("Migrations", "016_AddRequestResponseMessageContentPayload.sql");

        Assert.Contains("add Payload varbinary(max) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("add PayloadKind nvarchar(16) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("add ContentEncoding nvarchar(64) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CK_RequestResponses_MessageContentPayloadKind", script, StringComparison.Ordinal);
        Assert.Contains("CK_RequestResponses_MessageContentPayloadShape", script, StringComparison.Ordinal);
        Assert.Contains("RequestResponseMessageContentPayload-001", script, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageContent nvarchar(max) null", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FreshInstall_CreatesMediaNeutralRequestResponsePayloadColumns()
    {
        string script = ReadScript("Install", "002_CreatePhaseOneSchema.sql");

        Assert.Contains("Payload varbinary(max) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PayloadKind nvarchar(16) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ContentEncoding nvarchar(64) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MessageContent nvarchar(max) not null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CK_RequestResponses_MessageContentPayloadKind", script, StringComparison.Ordinal);
        Assert.Contains("CK_RequestResponses_MessageContentPayloadShape", script, StringComparison.Ordinal);
        Assert.Contains("RequestResponseMessageContentPayload-001", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_VerifiesMediaNeutralRequestResponsePayloadSchema()
    {
        string script = ReadScript("Validation", "ValidatePhaseOneSchema.sql");

        Assert.Contains("RequestResponseMessageContentPayload-001", script, StringComparison.Ordinal);
        Assert.Contains("RequestResponses.Payload", script, StringComparison.Ordinal);
        Assert.Contains("RequestResponses.PayloadKind", script, StringComparison.Ordinal);
        Assert.Contains("RequestResponses.ContentEncoding", script, StringComparison.Ordinal);
        Assert.Contains("CK_RequestResponses_MessageContentPayloadKind", script, StringComparison.Ordinal);
        Assert.Contains("CK_RequestResponses_MessageContentPayloadShape", script, StringComparison.Ordinal);
    }

    private static string ReadScript(params string[] segments)
    {
        return File.ReadAllText(Path.Combine([ScriptsRoot, .. segments]));
    }
}
