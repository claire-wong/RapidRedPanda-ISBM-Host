using RapidRedPanda.ISBM.Host.Messaging;
using System.Text.Json.Nodes;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class SqlMessageContentAdapterTests
{
    public static IEnumerable<object?[]> MessageContentCases()
    {
        yield return [InternalMessageContent.FromJson(BinaryData.FromString("""{"speed":1200}""")), """{"content":{"speed":1200}}""", null];
        yield return [InternalMessageContent.FromString("<speed>1200</speed>", "application/xml"), """{"mediaType":"application/xml","content":"<speed>1200</speed>"}""", "application/xml"];
        yield return [InternalMessageContent.FromString("speed=1200", "text/plain"), """{"mediaType":"text/plain","content":"speed=1200"}""", "text/plain"];
    }

    [Theory]
    [MemberData(nameof(MessageContentCases))]
    public void ToSql_SerializesCurrentResponsePersistenceShape(
        object contentValue,
        string expectedMessageContent,
        string? expectedMediaType)
    {
        InternalMessageContent content = Assert.IsType<InternalMessageContent>(contentValue);
        SqlMessageContent sql = SqlMessageContentAdapter.ToSql(content);

        Assert.Equal(expectedMediaType, sql.MediaType);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(expectedMessageContent),
            JsonNode.Parse(sql.MessageContent)));
    }

    [Theory]
    [MemberData(nameof(MessageContentCases))]
    public void TryFromSql_ReadsCurrentResponsePersistenceShape(
        object expectedValue,
        string serializedMessageContent,
        string? mediaType)
    {
        InternalMessageContent expected = Assert.IsType<InternalMessageContent>(expectedValue);
        bool read = SqlMessageContentAdapter.TryFromSql(
            serializedMessageContent,
            mediaType,
            out InternalMessageContent? actual);

        Assert.True(read);
        Assert.NotNull(actual);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.MediaType, actual.MediaType);
        Assert.Equal(expected.Payload.ToString(), actual.Payload.ToString());
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
}
