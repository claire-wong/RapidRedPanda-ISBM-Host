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
    public void Migration_CreatesNewColumnConstraintsThroughDynamicSql()
    {
        string script = ReadScript("Migrations", "016_AddRequestResponseMessageContentPayload.sql");

        Assert.Contains("exec sys.sp_executesql", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CK_RequestResponses_MessageContentPayloadKind", script, StringComparison.Ordinal);
        Assert.Contains("CK_RequestResponses_MessageContentPayloadShape", script, StringComparison.Ordinal);

        string staticBatch = RemoveDynamicSqlBodies(script);
        Assert.DoesNotContain(
            "add constraint CK_RequestResponses_MessageContentPayloadKind",
            staticBatch,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "add constraint CK_RequestResponses_MessageContentPayloadShape",
            staticBatch,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_IsGuardedForFirstRunAndSecondRunIdempotency()
    {
        string script = ReadScript("Migrations", "016_AddRequestResponseMessageContentPayload.sql");

        Assert.Contains("if exists (select 1 from dbo.SchemaVersions where VersionNumber = N'RequestResponseMessageContentPayload-001')", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("commit transaction", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("return", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if col_length(N'dbo.RequestResponses', N'Payload') is null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if col_length(N'dbo.RequestResponses', N'PayloadKind') is null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if col_length(N'dbo.RequestResponses', N'ContentEncoding') is null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name = N'CK_RequestResponses_MessageContentPayloadKind'", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name = N'CK_RequestResponses_MessageContentPayloadShape'", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublicationMessageContentMigration_AddsNullableContentColumnsAndConstraints()
    {
        if (!TryReadScript("Migrations", "020_AddPublicationMessageContent.sql", out string script))
        {
            return;
        }

        Assert.Contains("PublicationMessageContent-001", script, StringComparison.Ordinal);
        Assert.Contains("alter table dbo.ProviderPublications", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("add MessageContent nvarchar(max) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("add MediaType nvarchar(256) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("add Payload varbinary(max) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("add PayloadKind nvarchar(16) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("add ContentEncoding nvarchar(64) null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CK_ProviderPublications_MessageContentPayloadKind", script, StringComparison.Ordinal);
        Assert.Contains("CK_ProviderPublications_MessageContentPayloadShape", script, StringComparison.Ordinal);
        Assert.Contains("IX_ProviderPublicationRecipients_Consumer_Outstanding", script, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicationMessageContentMigration_IsGuardedAndIdempotent()
    {
        if (!TryReadScript("Migrations", "020_AddPublicationMessageContent.sql", out string script))
        {
            return;
        }

        Assert.Contains("if exists (select 1 from dbo.SchemaVersions where VersionNumber = N'PublicationMessageContent-001')", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("return", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if col_length(N'dbo.ProviderPublications', N'MessageContent') is null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if col_length(N'dbo.ProviderPublications', N'Payload') is null", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name = N'CK_ProviderPublications_MessageContentPayloadKind'", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name = N'CK_ProviderPublications_MessageContentPayloadShape'", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name = N'IX_ProviderPublicationRecipients_Consumer_Outstanding'", script, StringComparison.OrdinalIgnoreCase);
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

    private static bool TryReadScript(string scriptType, string scriptName, out string script)
    {
        string path = Path.Combine(ScriptsRoot, scriptType, scriptName);
        if (!File.Exists(path))
        {
            script = string.Empty;
            return false;
        }

        script = File.ReadAllText(path);
        return true;
    }

    private static string RemoveDynamicSqlBodies(string script)
    {
        string result = script;
        const string prefix = "exec sys.sp_executesql N'";

        while (true)
        {
            int start = result.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return result;
            }

            int index = start + prefix.Length;
            while (index < result.Length)
            {
                if (result[index] == '\'' &&
                    (index + 1 >= result.Length || result[index + 1] != '\''))
                {
                    result = result.Remove(start, index - start + 1);
                    break;
                }

                index += result[index] == '\'' && index + 1 < result.Length && result[index + 1] == '\''
                    ? 2
                    : 1;
            }
        }
    }
}
