using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRedPanda.ISBM.Host.Contracts;
using RapidRedPanda.ISBM.Host.Controllers;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Messaging;
using RapidRedPanda.ISBM.Host.Services;
using System.Text;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class MessageContentSemanticsControllerTests
{
    private const string NativeJsonEnvelope = """{"messageContent":{"content":{"speed":1200}},"topics":["Topic-A"]}""";
    private const string XmlStringEnvelope = """{"messageContent":{"mediaType":"application/xml","content":"<speed>1200</speed>"},"topics":["Topic-A"]}""";
    private const string TextStringEnvelope = """{"messageContent":{"mediaType":"text/plain","content":"speed=1200"},"topics":["Topic-A"]}""";
    private const string BinaryEnvelope = """{"messageContent":{"mediaType":"application/octet-stream","contentEncoding":"base64","content":"AAECAwQ="},"topics":["Topic-A"]}""";

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("application/vnd.example+json")]
    public async Task PostPublication_JsonEnvelopeContentType_AcceptsNativeJsonWithoutPayloadMediaType(string contentType)
    {
        PublicationService service = new();
        ProviderPublicationServiceController controller = CreatePublicationController(service);
        SetBody(controller, NativeJsonEnvelope, contentType);

        IActionResult result = await controller.PostPublication(Guid.NewGuid().ToString(), CancellationToken.None);

        ObjectResult created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(InternalMessageContentKind.Json, service.PostedMessageContent?.Kind);
        Assert.Null(service.PostedMessageContent?.MediaType);
        Assert.Equal("""{"speed":1200}""", service.PostedMessageContent?.Payload.ToString());
    }

    [Theory]
    [InlineData("application/xml")]
    [InlineData("text/plain")]
    [InlineData("application/octet-stream")]
    [InlineData(null)]
    [InlineData("not a media type")]
    public async Task PostPublication_NonJsonEnvelopeContentType_ReturnsUnsupportedMediaType(string? contentType)
    {
        PublicationService service = new();
        ProviderPublicationServiceController controller = CreatePublicationController(service);
        SetBody(controller, NativeJsonEnvelope, contentType);

        IActionResult result = await controller.PostPublication(Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.IsType<UnsupportedMediaTypeResult>(result);
        Assert.Null(service.PostedMessageContent);
    }

    [Theory]
    [InlineData(XmlStringEnvelope, "application/xml")]
    [InlineData(TextStringEnvelope, "text/plain")]
    public async Task PostPublication_StringContentWithMediaType_DelegatesPayloadMediaType(string envelope, string expectedMediaType)
    {
        PublicationService service = new();
        ProviderPublicationServiceController controller = CreatePublicationController(service);
        SetBody(controller, envelope, "application/json");

        IActionResult result = await controller.PostPublication(Guid.NewGuid().ToString(), CancellationToken.None);

        ObjectResult created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(InternalMessageContentKind.String, service.PostedMessageContent?.Kind);
        Assert.Equal(expectedMediaType, service.PostedMessageContent?.MediaType);
    }

    [Fact]
    public async Task PostPublication_BinaryContent_DelegatesDecodedPayloadAndPayloadMediaType()
    {
        PublicationService service = new();
        ProviderPublicationServiceController controller = CreatePublicationController(service);
        SetBody(controller, BinaryEnvelope, "application/json");

        IActionResult result = await controller.PostPublication(Guid.NewGuid().ToString(), CancellationToken.None);

        ObjectResult created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(InternalMessageContentKind.Binary, service.PostedMessageContent?.Kind);
        Assert.Equal("application/octet-stream", service.PostedMessageContent?.MediaType);
        Assert.Equal("base64", service.PostedMessageContent?.ContentEncoding);
        Assert.Equal([0, 1, 2, 3, 4], service.PostedMessageContent?.Payload.ToArray());
    }

    [Theory]
    [InlineData("""{"messageContent":{"mediaType":"application/json","content":{"speed":1200}},"topics":["Topic-A"]}""")]
    [InlineData("""{"messageContent":{"contentEncoding":"gzip","content":{"speed":1200}},"topics":["Topic-A"]}""")]
    [InlineData("""{"messageContent":{"content":"speed=1200"},"topics":["Topic-A"]}""")]
    [InlineData("""{"messageContent":{"mediaType":" ","content":"speed=1200"},"topics":["Topic-A"]}""")]
    [InlineData("""{"messageContent":{"mediaType":"application/octet-stream","contentEncoding":"base64","content":"not-base64"},"topics":["Topic-A"]}""")]
    [InlineData("""{"messageContent":{"mediaType":"application/octet-stream","contentEncoding":"gzip","content":"AAECAwQ="},"topics":["Topic-A"]}""")]
    [InlineData("""{"messageContent":{"mediaType":" ","contentEncoding":"base64","content":"AAECAwQ="},"topics":["Topic-A"]}""")]
    [InlineData("""{"messageContent":{"content":[1200]},"topics":["Topic-A"]}""")]
    public async Task PostPublication_InvalidMessageContentSemantics_ReturnsParameterFault(string envelope)
    {
        PublicationService service = new();
        ProviderPublicationServiceController controller = CreatePublicationController(service);
        SetBody(controller, envelope, "application/json");

        IActionResult result = await controller.PostPublication(Guid.NewGuid().ToString(), CancellationToken.None);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(badRequest.Value);
        Assert.Equal("ParameterFault", fault.Fault);
        Assert.Null(service.PostedMessageContent);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("application/vnd.example+json")]
    public async Task PostRequest_JsonEnvelopeContentType_AcceptsNativeJsonWithoutPayloadMediaType(string contentType)
    {
        ConsumerRequestService service = new();
        ConsumerRequestServiceController controller = CreateConsumerRequestController(service);
        SetBody(controller, NativeJsonEnvelope, contentType);

        IActionResult result = await controller.PostRequest(Guid.NewGuid().ToString(), CancellationToken.None);

        ObjectResult created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(InternalMessageContentKind.Json, service.PostedMessageContent?.Kind);
        Assert.Null(service.PostedMessageContent?.MediaType);
        Assert.Equal("""{"speed":1200}""", service.PostedMessageContent?.Payload.ToString());
    }

    [Theory]
    [InlineData("application/xml")]
    [InlineData("text/plain")]
    [InlineData("application/octet-stream")]
    [InlineData(null)]
    [InlineData("not a media type")]
    public async Task PostRequest_NonJsonEnvelopeContentType_ReturnsUnsupportedMediaType(string? contentType)
    {
        ConsumerRequestService service = new();
        ConsumerRequestServiceController controller = CreateConsumerRequestController(service);
        SetBody(controller, NativeJsonEnvelope, contentType);

        IActionResult result = await controller.PostRequest(Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.IsType<UnsupportedMediaTypeResult>(result);
        Assert.Null(service.PostedMessageContent);
    }

    [Theory]
    [InlineData(XmlStringEnvelope, "application/xml")]
    [InlineData(TextStringEnvelope, "text/plain")]
    public async Task PostRequest_StringContentWithMediaType_DelegatesPayloadMediaType(string envelope, string expectedMediaType)
    {
        ConsumerRequestService service = new();
        ConsumerRequestServiceController controller = CreateConsumerRequestController(service);
        SetBody(controller, envelope, "application/json");

        IActionResult result = await controller.PostRequest(Guid.NewGuid().ToString(), CancellationToken.None);

        ObjectResult created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(InternalMessageContentKind.String, service.PostedMessageContent?.Kind);
        Assert.Equal(expectedMediaType, service.PostedMessageContent?.MediaType);
    }

    [Theory]
    [InlineData("""{"messageContent":{"content":{"value":42}},"topics":["validation"]}""", InternalMessageContentKind.Json, null, null)]
    [InlineData("""{"messageContent":{"mediaType":"text/plain","content":"Motor temperature high"},"topics":["validation"]}""", InternalMessageContentKind.String, "text/plain", null)]
    [InlineData("""{"messageContent":{"mediaType":"application/xml","content":"<reading><value>42</value></reading>"},"topics":["validation"]}""", InternalMessageContentKind.String, "application/xml", null)]
    [InlineData("""{"messageContent":{"mediaType":"application/json","content":"{\"value\":42}"},"topics":["validation"]}""", InternalMessageContentKind.String, "application/json", null)]
    [InlineData("""{"messageContent":{"mediaType":"text/plain","content":"Motor temperature high"},"topics":["validation"],"expiry":"P2D"}""", InternalMessageContentKind.String, "text/plain", "P2D")]
    public async Task PostRequest_MessageContentWithOptionalExpiry_DelegatesExpectedContent(
        string envelope,
        InternalMessageContentKind expectedKind,
        string? expectedMediaType,
        string? expectedExpiry)
    {
        ConsumerRequestService service = new();
        ConsumerRequestServiceController controller = CreateConsumerRequestController(service);
        SetBody(controller, envelope, "application/json");

        IActionResult result = await controller.PostRequest(Guid.NewGuid().ToString(), CancellationToken.None);

        ObjectResult created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(expectedKind, service.PostedMessageContent?.Kind);
        Assert.Equal(expectedMediaType, service.PostedMessageContent?.MediaType);
        Assert.Equal(expectedExpiry, service.PostedExpiry);
    }

    [Fact]
    public async Task PostRequest_StringContentWithoutMediaType_ReturnsParameterFaultWithoutDelegating()
    {
        ConsumerRequestService service = new();
        ConsumerRequestServiceController controller = CreateConsumerRequestController(service);
        SetBody(controller, """{"messageContent":{"content":"Motor temperature high"},"topics":["validation"]}""", "application/json");

        IActionResult result = await controller.PostRequest(Guid.NewGuid().ToString(), CancellationToken.None);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(badRequest.Value);
        Assert.Equal("ParameterFault", fault.Fault);
        Assert.Null(service.PostedMessageContent);
    }

    [Fact]
    public async Task PostRequest_BinaryContent_DelegatesDecodedPayloadAndPayloadMediaType()
    {
        ConsumerRequestService service = new();
        ConsumerRequestServiceController controller = CreateConsumerRequestController(service);
        SetBody(controller, BinaryEnvelope, "application/json");

        IActionResult result = await controller.PostRequest(Guid.NewGuid().ToString(), CancellationToken.None);

        ObjectResult created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(InternalMessageContentKind.Binary, service.PostedMessageContent?.Kind);
        Assert.Equal("application/octet-stream", service.PostedMessageContent?.MediaType);
        Assert.Equal("base64", service.PostedMessageContent?.ContentEncoding);
        Assert.Equal([0, 1, 2, 3, 4], service.PostedMessageContent?.Payload.ToArray());
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("application/vnd.example+json")]
    public async Task PostResponse_JsonEnvelopeContentType_AcceptsNativeJsonWithoutPayloadMediaType(string contentType)
    {
        ProviderRequestService service = new();
        ProviderRequestServiceController controller = CreateProviderRequestController(service);
        SetBody(controller, """{"messageContent":{"content":{"accepted":true}}}""", contentType);

        IActionResult result = await controller.PostResponse(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), CancellationToken.None);

        ObjectResult created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(InternalMessageContentKind.Json, service.PostedMessageContent?.Kind);
        Assert.Null(service.PostedMessageContent?.MediaType);
        Assert.Equal("""{"accepted":true}""", service.PostedMessageContent?.Payload.ToString());
    }

    [Theory]
    [InlineData("application/xml")]
    [InlineData("text/plain")]
    [InlineData("application/octet-stream")]
    [InlineData(null)]
    [InlineData("not a media type")]
    public async Task PostResponse_NonJsonEnvelopeContentType_ReturnsUnsupportedMediaType(string? contentType)
    {
        ProviderRequestService service = new();
        ProviderRequestServiceController controller = CreateProviderRequestController(service);
        SetBody(controller, """{"messageContent":{"content":{"accepted":true}}}""", contentType);

        IActionResult result = await controller.PostResponse(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.IsType<UnsupportedMediaTypeResult>(result);
        Assert.Null(service.PostedMessageContent);
    }

    [Theory]
    [InlineData("""{"messageContent":{"mediaType":"application/xml","content":"<accepted>true</accepted>"}}""", "application/xml")]
    [InlineData("""{"messageContent":{"mediaType":"text/plain","content":"accepted=true"}}""", "text/plain")]
    public async Task PostResponse_StringContentWithMediaType_DelegatesPayloadMediaType(string envelope, string expectedMediaType)
    {
        ProviderRequestService service = new();
        ProviderRequestServiceController controller = CreateProviderRequestController(service);
        SetBody(controller, envelope, "application/json");

        IActionResult result = await controller.PostResponse(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), CancellationToken.None);

        ObjectResult created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(InternalMessageContentKind.String, service.PostedMessageContent?.Kind);
        Assert.Equal(expectedMediaType, service.PostedMessageContent?.MediaType);
    }

    [Fact]
    public async Task PostResponse_BinaryContent_DelegatesDecodedPayloadAndPayloadMediaType()
    {
        ProviderRequestService service = new();
        ProviderRequestServiceController controller = CreateProviderRequestController(service);
        SetBody(controller, """{"messageContent":{"mediaType":"application/octet-stream","contentEncoding":"base64","content":"AAECAwQ="}}""", "application/json");

        IActionResult result = await controller.PostResponse(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), CancellationToken.None);

        ObjectResult created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(InternalMessageContentKind.Binary, service.PostedMessageContent?.Kind);
        Assert.Equal("application/octet-stream", service.PostedMessageContent?.MediaType);
        Assert.Equal("base64", service.PostedMessageContent?.ContentEncoding);
        Assert.Equal([0, 1, 2, 3, 4], service.PostedMessageContent?.Payload.ToArray());
    }

    [Theory]
    [InlineData("""{"messageContent":{"mediaType":"application/json","content":{"accepted":true}}}""")]
    [InlineData("""{"messageContent":{"contentEncoding":"gzip","content":{"accepted":true}}}""")]
    [InlineData("""{"messageContent":{"content":"accepted=true"}}""")]
    [InlineData("""{"messageContent":{"mediaType":"application/octet-stream","contentEncoding":"base64","content":"not-base64"}}""")]
    [InlineData("""{"messageContent":{"mediaType":"application/octet-stream","contentEncoding":"gzip","content":"AAECAwQ="}}""")]
    [InlineData("""{"messageContent":{"mediaType":" ","contentEncoding":"base64","content":"AAECAwQ="}}""")]
    public async Task PostResponse_InvalidMessageContentSemantics_ReturnsParameterFault(string envelope)
    {
        ProviderRequestService service = new();
        ProviderRequestServiceController controller = CreateProviderRequestController(service);
        SetBody(controller, envelope, "application/json");

        IActionResult result = await controller.PostResponse(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), CancellationToken.None);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(badRequest.Value);
        Assert.Equal("ParameterFault", fault.Fault);
        Assert.Null(service.PostedMessageContent);
    }

    private static ProviderPublicationServiceController CreatePublicationController(PublicationService service)
    {
        ProviderPublicationServiceController controller = new(
            service,
            new NoOpUsernameTokenParser(),
            NullLogger<ProviderPublicationServiceController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static ConsumerRequestServiceController CreateConsumerRequestController(ConsumerRequestService service)
    {
        ConsumerRequestServiceController controller = new(
            service,
            new NoOpUsernameTokenParser(),
            NullLogger<ConsumerRequestServiceController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static ProviderRequestServiceController CreateProviderRequestController(ProviderRequestService service)
    {
        ProviderRequestServiceController controller = new(
            service,
            new NoOpUsernameTokenParser(),
            NullLogger<ProviderRequestServiceController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static void SetBody(Controller controller, string json, string? contentType)
    {
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        if (contentType is not null)
        {
            controller.Request.ContentType = contentType;
        }
    }

    private sealed class PublicationService : IProviderPublicationSessionService
    {
        public InternalMessageContent? PostedMessageContent { get; private set; }

        public Task<OpenPublicationSessionResult> OpenPublicationSessionAsync(
            string channelId,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ClosePublicationSessionResult> ClosePublicationSessionAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PostPublicationResult> PostPublicationAsync(
            string sessionId,
            IReadOnlyList<string?>? topics,
            InternalMessageContent messageContent,
            string? expiry,
            CancellationToken cancellationToken)
        {
            PostedMessageContent = messageContent;
            return Task.FromResult(PostPublicationResult.Success(Guid.NewGuid()));
        }
    }

    private sealed class ConsumerRequestService : IConsumerRequestSessionService
    {
        public InternalMessageContent? PostedMessageContent { get; private set; }
        public string? PostedExpiry { get; private set; }

        public Task<OpenConsumerRequestSessionResult> OpenConsumerRequestSessionAsync(
            string channelId,
            string? listenerUrl,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CloseConsumerRequestSessionServiceResult> CloseConsumerRequestSessionAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PostRequestResult> PostRequestAsync(
            string sessionId,
            IReadOnlyList<string?>? topics,
            InternalMessageContent messageContent,
            string? expiry,
            CancellationToken cancellationToken)
        {
            PostedMessageContent = messageContent;
            PostedExpiry = expiry;
            return Task.FromResult(PostRequestResult.Success(Guid.NewGuid()));
        }

        public Task<ExpireRequestResult> ExpireRequestAsync(
            string sessionId,
            string messageId,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ReadResponseResult> ReadResponseAsync(
            string sessionId,
            string requestMessageId,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<RemoveResponseResult> RemoveResponseAsync(
            string sessionId,
            string requestMessageId,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ProviderRequestService : IProviderRequestSessionService
    {
        public InternalMessageContent? PostedMessageContent { get; private set; }

        public Task<OpenProviderRequestSessionResult> OpenProviderRequestSessionAsync(
            string channelId,
            IReadOnlyList<string> topics,
            string? listenerUrl,
            string? filterExpressionsJson,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CloseProviderRequestSessionResult> CloseProviderRequestSessionAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ReadRequestResult> ReadRequestAsync(string sessionId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<RemoveRequestResult> RemoveRequestAsync(string sessionId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PostResponseResult> PostResponseAsync(
            string sessionId,
            string requestMessageId,
            InternalMessageContent messageContent,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            PostedMessageContent = messageContent;
            return Task.FromResult(PostResponseResult.Success(Guid.NewGuid()));
        }
    }

    private sealed class NoOpUsernameTokenParser : IUsernameTokenParser
    {
        public IReadOnlyList<UsernameTokenInput> Parse(IReadOnlyList<SecurityTokenRequest>? securityTokens)
        {
            return [];
        }

        public string NormalizeUsername(string username)
        {
            return username;
        }
    }
}
