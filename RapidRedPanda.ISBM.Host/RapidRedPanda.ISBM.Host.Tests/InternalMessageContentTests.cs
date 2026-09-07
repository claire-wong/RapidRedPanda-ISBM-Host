using RapidRedPanda.ISBM.Host.Messaging;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class InternalMessageContentTests
{
    [Fact]
    public void FromJson_ValidObjectPayload_CreatesJsonContentWithoutMediaTypeOrEncoding()
    {
        InternalMessageContent content = InternalMessageContent.FromJson(BinaryData.FromString("""{"speed":1200}"""));

        Assert.Equal(InternalMessageContentKind.Json, content.Kind);
        Assert.Equal("""{"speed":1200}""", content.Payload.ToString());
        Assert.Null(content.MediaType);
        Assert.Null(content.ContentEncoding);
    }

    [Theory]
    [InlineData("""[1200]""")]
    [InlineData("1200")]
    [InlineData("\"speed\"")]
    public void FromJson_NonObjectPayload_ThrowsArgumentException(string payload)
    {
        Assert.Throws<ArgumentException>(() => InternalMessageContent.FromJson(BinaryData.FromString(payload)));
    }

    [Fact]
    public void FromString_ValidContent_CreatesStringContentWithOriginalMediaType()
    {
        InternalMessageContent content = InternalMessageContent.FromString("<speed>1200</speed>", "Application/XML; Charset=UTF-8");

        Assert.Equal(InternalMessageContentKind.String, content.Kind);
        Assert.Equal("<speed>1200</speed>", content.Payload.ToString());
        Assert.Equal("Application/XML; Charset=UTF-8", content.MediaType);
        Assert.Null(content.ContentEncoding);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FromString_MissingMediaType_ThrowsArgumentException(string mediaType)
    {
        Assert.Throws<ArgumentException>(() => InternalMessageContent.FromString("speed=1200", mediaType));
    }

    [Fact]
    public void FromBinary_ValidContent_CreatesBinaryContentWithDecodedPayloadAndEncoding()
    {
        InternalMessageContent content = InternalMessageContent.FromBinary(
            BinaryData.FromBytes([0, 1, 2, 3]),
            "application/octet-stream",
            "base64");

        Assert.Equal(InternalMessageContentKind.Binary, content.Kind);
        Assert.Equal([0, 1, 2, 3], content.Payload.ToArray());
        Assert.Equal("application/octet-stream", content.MediaType);
        Assert.Equal("base64", content.ContentEncoding);
    }

    [Fact]
    public void FromBinary_WithoutMediaType_CreatesBinaryContentWithNullMediaType()
    {
        InternalMessageContent content = InternalMessageContent.FromBinary(
            BinaryData.FromBytes([0, 1, 2, 3]),
            null,
            "base64");

        Assert.Equal(InternalMessageContentKind.Binary, content.Kind);
        Assert.Equal([0, 1, 2, 3], content.Payload.ToArray());
        Assert.Null(content.MediaType);
        Assert.Equal("base64", content.ContentEncoding);
    }

    [Fact]
    public void FromBinary_EmptyPayload_CreatesBinaryContentWithEmptyDecodedPayload()
    {
        InternalMessageContent content = InternalMessageContent.FromBinary(
            BinaryData.FromBytes([]),
            null,
            "base64");

        Assert.Equal(InternalMessageContentKind.Binary, content.Kind);
        Assert.Empty(content.Payload.ToArray());
        Assert.Null(content.MediaType);
        Assert.Equal("base64", content.ContentEncoding);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FromBinary_MissingContentEncoding_ThrowsArgumentException(string contentEncoding)
    {
        Assert.Throws<ArgumentException>(() => InternalMessageContent.FromBinary(
            BinaryData.FromBytes([0]),
            null,
            contentEncoding));
    }
}
