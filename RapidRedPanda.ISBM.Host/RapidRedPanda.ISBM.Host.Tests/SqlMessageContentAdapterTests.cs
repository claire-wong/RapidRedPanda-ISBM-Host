using RapidRedPanda.ISBM.Host.Messaging;
using RapidRedPanda.ISBM.Host.Data;
using System.Text;
using System.Text.Json.Nodes;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class SqlMessageContentAdapterTests
{
    public static IEnumerable<object?[]> MessageContentCases()
    {
        yield return [InternalMessageContent.FromJson(BinaryData.FromString("""{"speed":1200}""")), """{"content":{"speed":1200}}""", null, "Json", null, Encoding.UTF8.GetBytes("""{"speed":1200}""")];
        yield return [InternalMessageContent.FromString("<speed>1200</speed>", "application/xml"), """{"mediaType":"application/xml","content":"<speed>1200</speed>"}""", "application/xml", "String", null, Encoding.UTF8.GetBytes("<speed>1200</speed>")];
        yield return [InternalMessageContent.FromString("speed=1200", "text/plain"), """{"mediaType":"text/plain","content":"speed=1200"}""", "text/plain", "String", null, Encoding.UTF8.GetBytes("speed=1200")];
        yield return [InternalMessageContent.FromBinary(BinaryData.FromBytes([0, 1, 2, 3, 4]), "application/octet-stream", "base64"), """{"mediaType":"application/octet-stream","contentEncoding":"base64","content":"AAECAwQ="}""", "application/octet-stream", "Binary", "base64", new byte[] { 0, 1, 2, 3, 4 }];
        yield return [InternalMessageContent.FromBinary(BinaryData.FromBytes([0, 1, 2, 3, 4]), null, "base64"), """{"contentEncoding":"base64","content":"AAECAwQ="}""", null, "Binary", "base64", new byte[] { 0, 1, 2, 3, 4 }];
    }

    [Theory]
    [MemberData(nameof(MessageContentCases))]
    public void ToSql_WritesLegacyAndMediaNeutralResponsePersistenceShape(
        object contentValue,
        string expectedMessageContent,
        string? expectedMediaType,
        string expectedPayloadKind,
        string? expectedContentEncoding,
        byte[] expectedPayload)
    {
        InternalMessageContent content = Assert.IsType<InternalMessageContent>(contentValue);
        PersistedMessageContent sql = SqlMessageContentAdapter.ToSql(content);

        Assert.Equal(expectedMediaType, sql.MediaType);
        Assert.Equal(expectedPayloadKind, sql.PayloadKind);
        Assert.Equal(expectedContentEncoding, sql.ContentEncoding);
        Assert.Equal(expectedPayload, sql.Payload);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(expectedMessageContent),
            JsonNode.Parse(sql.LegacyMessageContent)));
    }

    [Theory]
    [MemberData(nameof(MessageContentCases))]
    public void TryFromSql_ReadsNewResponsePersistenceShape(
        object expectedValue,
        string serializedMessageContent,
        string? mediaType,
        string payloadKind,
        string? contentEncoding,
        byte[] payload)
    {
        InternalMessageContent expected = Assert.IsType<InternalMessageContent>(expectedValue);
        bool read = SqlMessageContentAdapter.TryFromSql(
            new PersistedMessageContent(serializedMessageContent, mediaType, payload, payloadKind, contentEncoding),
            out InternalMessageContent? actual);

        Assert.True(read);
        Assert.NotNull(actual);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.MediaType, actual.MediaType);
        Assert.Equal(expected.ContentEncoding, actual.ContentEncoding);
        Assert.Equal(expected.Payload.ToArray(), actual.Payload.ToArray());
    }

    [Theory]
    [InlineData("""{"content":{"speed":1200}}""", null, InternalMessageContentKind.Json)]
    [InlineData("""{"mediaType":"application/xml","content":"<speed>1200</speed>"}""", "application/xml", InternalMessageContentKind.String)]
    public void TryFromSql_ReadsLegacyResponsePersistenceShape(
        string serializedMessageContent,
        string? mediaType,
        InternalMessageContentKind expectedKind)
    {
        bool read = SqlMessageContentAdapter.TryFromSql(
            new PersistedMessageContent(serializedMessageContent, mediaType, null, null, null),
            out InternalMessageContent? actual);

        Assert.True(read);
        Assert.NotNull(actual);
        Assert.Equal(expectedKind, actual.Kind);
    }

    [Fact]
    public void TryFromSql_MediaTypeMismatch_ReturnsFalse()
    {
        bool read = SqlMessageContentAdapter.TryFromSql(
            """{"mediaType":"application/xml","content":"<speed>1200</speed>"}""",
            "text/plain",
            out InternalMessageContent? content);

        Assert.False(read);
        Assert.Null(content);
    }

    [Theory]
    [InlineData(null, "Json", null)]
    [InlineData(new byte[] { 0 }, null, null)]
    [InlineData(new byte[] { 0 }, "Json", "base64")]
    [InlineData(new byte[] { 0 }, "String", null)]
    [InlineData(new byte[] { 0 }, "Binary", null)]
    [InlineData(new byte[] { 0 }, "Unknown", null)]
    public void TryFromSql_InvalidNewPersistenceShape_ReturnsFalse(
        byte[]? payload,
        string? payloadKind,
        string? contentEncoding)
    {
        bool read = SqlMessageContentAdapter.TryFromSql(
            new PersistedMessageContent("""{"content":{"legacy":true}}""", null, payload, payloadKind, contentEncoding),
            out InternalMessageContent? content);

        Assert.False(read);
        Assert.Null(content);
    }

    [Fact]
    public void TryFromSql_BinaryWithBlankMediaType_ReturnsFalse()
    {
        bool read = SqlMessageContentAdapter.TryFromSql(
            new PersistedMessageContent(
                """{"content":{"legacy":true}}""",
                " ",
                [0],
                "Binary",
                "base64"),
            out InternalMessageContent? content);

        Assert.False(read);
        Assert.Null(content);
    }

    [Fact]
    public void TryFromSql_ConflictingCompleteRepresentations_UsesNewRepresentation()
    {
        bool read = SqlMessageContentAdapter.TryFromSql(
            new PersistedMessageContent(
                """{"content":{"legacy":true}}""",
                null,
                Encoding.UTF8.GetBytes("""{"new":true}"""),
                "Json",
                null),
            out InternalMessageContent? content);

        Assert.True(read);
        Assert.NotNull(content);
        Assert.Equal("""{"new":true}""", content.Payload.ToString());
    }
}
