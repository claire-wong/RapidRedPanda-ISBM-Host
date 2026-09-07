using RapidRedPanda.ISBM.Host.Messaging;
using RapidRedPanda.ISBM.Host.Services;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class BrokerMessageContentAdapterTests
{
    public static IEnumerable<object?[]> MessageContentCases()
    {
        yield return [InternalMessageContent.FromJson(BinaryData.FromString("""{"speed":1200}""")), """{"content":{"speed":1200}}"""];
        yield return [InternalMessageContent.FromString("<speed>1200</speed>", "application/xml"), """{"mediaType":"application/xml","content":"<speed>1200</speed>"}"""];
        yield return [InternalMessageContent.FromString("speed=1200", "text/plain"), """{"mediaType":"text/plain","content":"speed=1200"}"""];
        yield return [InternalMessageContent.FromBinary(BinaryData.FromBytes([0, 1, 2, 3, 4]), "application/octet-stream", "base64"), """{"mediaType":"application/octet-stream","contentEncoding":"base64","content":"AAECAwQ="}"""];
        yield return [InternalMessageContent.FromBinary(BinaryData.FromBytes([0, 1, 2, 3, 4]), null, "base64"), """{"contentEncoding":"base64","content":"AAECAwQ="}"""];
    }

    [Theory]
    [MemberData(nameof(MessageContentCases))]
    public void CreateEnvelopeBody_EmbedsCurrentBrokerMessageContentShape(
        object contentValue,
        string expectedMessageContent)
    {
        Guid messageId = Guid.NewGuid();
        InternalMessageContent content = Assert.IsType<InternalMessageContent>(contentValue);

        byte[] body = BrokerMessageContentAdapter.CreateEnvelopeBody(messageId, content, ["Topic-A"]);

        using JsonDocument document = JsonDocument.Parse(body);
        Assert.Equal(messageId.ToString("D"), document.RootElement.GetProperty("messageId").GetString());
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(expectedMessageContent),
            JsonNode.Parse(document.RootElement.GetProperty("messageContent").GetRawText())));
        Assert.Equal("Topic-A", document.RootElement.GetProperty("topics")[0].GetString());
    }

    [Theory]
    [MemberData(nameof(MessageContentCases))]
    public void TryReadMessageContent_ReadsCurrentBrokerEnvelope(
        object expectedValue,
        string expectedMessageContent)
    {
        Guid messageId = Guid.NewGuid();
        InternalMessageContent expected = Assert.IsType<InternalMessageContent>(expectedValue);
        TransportPublication publication = new(
            messageId,
            ["Topic-A"],
            expected.MediaType,
            BinaryData.FromBytes(BrokerMessageContentAdapter.CreateEnvelopeBody(messageId, expected, ["Topic-A"])));

        bool read = BrokerMessageContentAdapter.TryReadMessageContent(publication, out InternalMessageContent? actual);

        Assert.True(read);
        Assert.NotNull(actual);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.MediaType, actual.MediaType);
        Assert.Equal(expected.ContentEncoding, actual.ContentEncoding);
        if (expected.Kind is InternalMessageContentKind.Binary)
        {
            Assert.Equal(expected.Payload.ToArray(), actual.Payload.ToArray());
            Assert.True(JsonNode.DeepEquals(
                JsonNode.Parse(expectedMessageContent),
                JsonNode.Parse(RestMessageContentMapper.ToRestJsonString(actual))));
        }
        else
        {
            Assert.True(JsonNode.DeepEquals(
                JsonNode.Parse(expectedMessageContent),
                JsonNode.Parse(RestMessageContentMapper.ToRestJsonString(actual))));
        }
    }
}
