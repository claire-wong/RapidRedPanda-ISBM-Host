using RapidRedPanda.ISBM.Host.Messaging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class RestMessageContentMapperTests
{
    [Fact]
    public void TryFromRest_NativeJson_MapsInnerContentObjectOnly()
    {
        using JsonDocument document = JsonDocument.Parse("""{"content":{"speed":1200}}""");

        bool mapped = RestMessageContentMapper.TryFromRest(document.RootElement, out InternalMessageContent? content);

        Assert.True(mapped);
        Assert.NotNull(content);
        Assert.Equal(InternalMessageContentKind.Json, content.Kind);
        Assert.Equal("""{"speed":1200}""", content.Payload.ToString());
        Assert.Null(content.MediaType);
        Assert.Null(content.ContentEncoding);
    }

    [Fact]
    public void ToRestJsonElement_JsonContent_RecreatesMessageContentWrapper()
    {
        InternalMessageContent content = InternalMessageContent.FromJson(BinaryData.FromString("""{"speed":1200}"""));

        JsonElement rest = RestMessageContentMapper.ToRestJsonElement(content);

        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""{"content":{"speed":1200}}"""),
            JsonNode.Parse(rest.GetRawText())));
    }

    [Theory]
    [InlineData("""{"mediaType":"application/xml","content":"<speed>1200</speed>"}""", "application/xml", "<speed>1200</speed>")]
    [InlineData("""{"mediaType":"Text/Plain; Charset=UTF-8","content":"speed=1200"}""", "Text/Plain; Charset=UTF-8", "speed=1200")]
    public void TryFromRest_StringContent_PreservesStringAndMediaType(
        string restJson,
        string expectedMediaType,
        string expectedContent)
    {
        using JsonDocument document = JsonDocument.Parse(restJson);

        bool mapped = RestMessageContentMapper.TryFromRest(document.RootElement, out InternalMessageContent? content);

        Assert.True(mapped);
        Assert.NotNull(content);
        Assert.Equal(InternalMessageContentKind.String, content.Kind);
        Assert.Equal(expectedContent, content.Payload.ToString());
        Assert.Equal(expectedMediaType, content.MediaType);
        Assert.Null(content.ContentEncoding);
    }

    [Fact]
    public void ToRestJsonElement_StringContent_RecreatesStringMessageContent()
    {
        InternalMessageContent content = InternalMessageContent.FromString("<speed>1200</speed>", "application/xml");

        JsonElement rest = RestMessageContentMapper.ToRestJsonElement(content);

        Assert.Equal("application/xml", rest.GetProperty("mediaType").GetString());
        Assert.Equal("<speed>1200</speed>", rest.GetProperty("content").GetString());
    }

    [Theory]
    [InlineData("""{"mediaType":"application/octet-stream","contentEncoding":"base64","content":"AAECAwQ="}""", "application/octet-stream", new byte[] { 0, 1, 2, 3, 4 })]
    [InlineData("""{"contentEncoding":"base64","content":"AAECAwQ="}""", null, new byte[] { 0, 1, 2, 3, 4 })]
    [InlineData("""{"contentEncoding":"BASE64","content":"AAECAwQ="}""", null, new byte[] { 0, 1, 2, 3, 4 })]
    [InlineData("""{"contentEncoding":"Base64","content":""}""", null, new byte[] { })]
    public void TryFromRest_BinaryContent_DecodesBase64Payload(
        string restJson,
        string? expectedMediaType,
        byte[] expectedPayload)
    {
        using JsonDocument document = JsonDocument.Parse(restJson);

        bool mapped = RestMessageContentMapper.TryFromRest(document.RootElement, out InternalMessageContent? content);

        Assert.True(mapped);
        Assert.NotNull(content);
        Assert.Equal(InternalMessageContentKind.Binary, content.Kind);
        Assert.Equal(expectedPayload, content.Payload.ToArray());
        Assert.Equal(expectedMediaType, content.MediaType);
        Assert.Equal("base64", content.ContentEncoding);
    }

    [Fact]
    public void ToRestJsonElement_BinaryContent_RecreatesCanonicalBinaryMessageContent()
    {
        InternalMessageContent content = InternalMessageContent.FromBinary(
            BinaryData.FromBytes([0, 1, 2, 3, 4]),
            "application/octet-stream",
            "base64");

        JsonElement rest = RestMessageContentMapper.ToRestJsonElement(content);

        Assert.Equal("application/octet-stream", rest.GetProperty("mediaType").GetString());
        Assert.Equal("base64", rest.GetProperty("contentEncoding").GetString());
        Assert.Equal("AAECAwQ=", rest.GetProperty("content").GetString());
    }

    [Fact]
    public void ToRestJsonElement_BinaryContentWithoutMediaType_OmitsMediaType()
    {
        InternalMessageContent content = InternalMessageContent.FromBinary(
            BinaryData.FromBytes([0, 1, 2, 3, 4]),
            null,
            "base64");

        JsonElement rest = RestMessageContentMapper.ToRestJsonElement(content);

        Assert.False(rest.TryGetProperty("mediaType", out _));
        Assert.Equal("base64", rest.GetProperty("contentEncoding").GetString());
        Assert.Equal("AAECAwQ=", rest.GetProperty("content").GetString());
    }

    [Theory]
    [InlineData("""{"mediaType":"application/json","content":{"speed":1200}}""")]
    [InlineData("""{"contentEncoding":"gzip","content":{"speed":1200}}""")]
    [InlineData("""{"content":"speed=1200"}""")]
    [InlineData("""{"mediaType":" ","content":"speed=1200"}""")]
    [InlineData("""{"mediaType":"application/octet-stream","contentEncoding":"gzip","content":"AAECAwQ="}""")]
    [InlineData("""{"mediaType":"application/octet-stream","contentEncoding":"base64","content":"not-base64"}""")]
    [InlineData("""{"mediaType":" ","contentEncoding":"base64","content":"AAECAwQ="}""")]
    [InlineData("""{"contentEncoding":" ","content":"AAECAwQ="}""")]
    [InlineData("""{"contentEncoding":"base64","content":null}""")]
    [InlineData("""{"content":[1200]}""")]
    public void TryFromRest_InvalidOrEncodedContent_ReturnsFalse(string restJson)
    {
        using JsonDocument document = JsonDocument.Parse(restJson);

        bool mapped = RestMessageContentMapper.TryFromRest(document.RootElement, out InternalMessageContent? content);

        Assert.False(mapped);
        Assert.Null(content);
    }
}
