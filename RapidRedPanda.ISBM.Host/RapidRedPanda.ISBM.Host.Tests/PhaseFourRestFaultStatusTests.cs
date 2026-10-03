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

public sealed class PhaseFourRestFaultStatusTests
{
    [Fact]
    public async Task ReadPublication_NoMessage_ReturnsNotFoundMessageFault()
    {
        ConsumerPublicationServiceController controller = CreateConsumerPublicationController(new ConsumerPublicationService
        {
            ReadResult = ReadPublicationResult.NoPublication()
        });

        IActionResult result = await controller.ReadPublication(Guid.NewGuid().ToString("D"), CancellationToken.None);

        AssertFault<NotFoundObjectResult>(result, "MessageFault");
    }

    [Fact]
    public async Task ReadRequest_NoMessage_ReturnsNotFoundMessageFault()
    {
        ProviderRequestServiceController controller = CreateProviderRequestController(new ProviderRequestService
        {
            ReadResult = ReadRequestResult.NoRequest()
        });

        IActionResult result = await controller.ReadRequest(Guid.NewGuid().ToString("D"), CancellationToken.None);

        AssertFault<NotFoundObjectResult>(result, "MessageFault");
    }

    [Fact]
    public async Task ReadResponse_NoMessage_ReturnsNotFoundMessageFault()
    {
        ConsumerRequestServiceController controller = CreateConsumerRequestController(new ConsumerRequestService
        {
            ReadResult = ReadResponseResult.NoResponse()
        });

        IActionResult result = await controller.ReadResponse(
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            CancellationToken.None);

        AssertFault<NotFoundObjectResult>(result, "MessageFault");
    }

    [Fact]
    public async Task PostResponse_NoMatchingRequest_ReturnsNotFoundMessageFault()
    {
        ProviderRequestServiceController controller = CreateProviderRequestController(new ProviderRequestService
        {
            PostResponseResult = PostResponseResult.NoAction()
        });
        SetJsonBody(controller, """{"messageContent":{"content":{"reading":42}}}""");

        IActionResult result = await controller.PostResponse(
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            CancellationToken.None);

        AssertFault<NotFoundObjectResult>(result, "MessageFault");
    }

    [Fact]
    public async Task OpenPublicationSession_WrongChannelType_ReturnsUnprocessableOperationFault()
    {
        ProviderPublicationServiceController controller = CreateProviderPublicationController(new ProviderPublicationService
        {
            OpenResult = OpenPublicationSessionResult.Failure(OpenPublicationSessionFailureKind.InvalidChannelType)
        });

        IActionResult result = await controller.OpenPublicationSession("%2Frequest%2Fchannel", CancellationToken.None);

        AssertFault<UnprocessableEntityObjectResult>(result, "OperationFault");
    }

    [Fact]
    public async Task OpenSubscriptionSession_WrongChannelType_ReturnsUnprocessableOperationFault()
    {
        ConsumerPublicationServiceController controller = CreateConsumerPublicationController(new ConsumerPublicationService
        {
            OpenResult = OpenSubscriptionSessionResult.Failure(OpenSubscriptionSessionFailureKind.InvalidChannelType)
        });
        SetJsonBody(controller, """{"topics":["Topic-A"]}""");

        IActionResult result = await controller.OpenSubscriptionSession("%2Frequest%2Fchannel", CancellationToken.None);

        AssertFault<UnprocessableEntityObjectResult>(result, "OperationFault");
    }

    [Fact]
    public async Task OpenProviderRequestSession_WrongChannelType_ReturnsUnprocessableOperationFault()
    {
        ProviderRequestServiceController controller = CreateProviderRequestController(new ProviderRequestService
        {
            OpenResult = OpenProviderRequestSessionResult.Failure(OpenProviderRequestSessionFailureKind.InvalidChannelType)
        });
        SetJsonBody(controller, """{"topics":["Topic-A"]}""");

        IActionResult result = await controller.OpenProviderRequestSession("%2Fpublication%2Fchannel", CancellationToken.None);

        AssertFault<UnprocessableEntityObjectResult>(result, "OperationFault");
    }

    [Fact]
    public async Task OpenConsumerRequestSession_WrongChannelType_ReturnsUnprocessableOperationFault()
    {
        ConsumerRequestServiceController controller = CreateConsumerRequestController(new ConsumerRequestService
        {
            OpenResult = OpenConsumerRequestSessionResult.Failure(OpenConsumerRequestSessionFailureKind.InvalidChannelType)
        });
        SetJsonBody(controller, "{}");

        IActionResult result = await controller.OpenConsumerRequestSession("%2Fpublication%2Fchannel", CancellationToken.None);

        AssertFault<UnprocessableEntityObjectResult>(result, "OperationFault");
    }

    private static ProviderPublicationServiceController CreateProviderPublicationController(ProviderPublicationService service)
    {
        ProviderPublicationServiceController controller = new(
            service,
            new NoOpUsernameTokenParser(),
            NullLogger<ProviderPublicationServiceController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static ConsumerPublicationServiceController CreateConsumerPublicationController(ConsumerPublicationService service)
    {
        ConsumerPublicationServiceController controller = new(
            service,
            new NoOpUsernameTokenParser(),
            NullLogger<ConsumerPublicationServiceController>.Instance);
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

    private static ConsumerRequestServiceController CreateConsumerRequestController(ConsumerRequestService service)
    {
        ConsumerRequestServiceController controller = new(
            service,
            new NoOpUsernameTokenParser(),
            NullLogger<ConsumerRequestServiceController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static void SetJsonBody(Controller controller, string json)
    {
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        controller.Request.ContentType = "application/json";
    }

    private static void AssertFault<TResult>(IActionResult result, string expectedFault)
        where TResult : ObjectResult
    {
        TResult objectResult = Assert.IsType<TResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(objectResult.Value);
        Assert.Equal(expectedFault, fault.Fault);
    }

    private sealed class ProviderPublicationService : IProviderPublicationSessionService
    {
        public OpenPublicationSessionResult OpenResult { get; init; } =
            OpenPublicationSessionResult.Success(Guid.NewGuid());

        public Task<OpenPublicationSessionResult> OpenPublicationSessionAsync(
            string channelId,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken) =>
            Task.FromResult(OpenResult);

        public Task<ClosePublicationSessionResult> ClosePublicationSessionAsync(string sessionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PostPublicationResult> PostPublicationAsync(
            string sessionId,
            IReadOnlyList<string?>? topics,
            InternalMessageContent messageContent,
            string? expiry,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ConsumerPublicationService : IConsumerSubscriptionSessionService
    {
        public OpenSubscriptionSessionResult OpenResult { get; init; } =
            OpenSubscriptionSessionResult.Success(Guid.NewGuid());
        public ReadPublicationResult ReadResult { get; init; } = ReadPublicationResult.NoPublication();

        public Task<OpenSubscriptionSessionResult> OpenSubscriptionSessionAsync(
            string channelId,
            IReadOnlyList<string> topics,
            string? listenerUrl,
            string? filterExpressionsJson,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken) =>
            Task.FromResult(OpenResult);

        public Task<CloseConsumerSubscriptionSessionResult> CloseSubscriptionSessionAsync(string sessionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReadPublicationResult> ReadPublicationAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(ReadResult);

        public Task<RemovePublicationResult> RemovePublicationAsync(string sessionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ProviderRequestService : IProviderRequestSessionService
    {
        public OpenProviderRequestSessionResult OpenResult { get; init; } =
            OpenProviderRequestSessionResult.Success(Guid.NewGuid());
        public ReadRequestResult ReadResult { get; init; } = ReadRequestResult.NoRequest();
        public PostResponseResult PostResponseResult { get; init; } = PostResponseResult.Success(Guid.NewGuid());

        public Task<OpenProviderRequestSessionResult> OpenProviderRequestSessionAsync(
            string channelId,
            IReadOnlyList<string> topics,
            string? listenerUrl,
            string? filterExpressionsJson,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken) =>
            Task.FromResult(OpenResult);

        public Task<CloseProviderRequestSessionResult> CloseProviderRequestSessionAsync(string sessionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReadRequestResult> ReadRequestAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(ReadResult);

        public Task<RemoveRequestResult> RemoveRequestAsync(string sessionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PostResponseResult> PostResponseAsync(
            string sessionId,
            string requestMessageId,
            InternalMessageContent messageContent,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken) =>
            Task.FromResult(PostResponseResult);
    }

    private sealed class ConsumerRequestService : IConsumerRequestSessionService
    {
        public OpenConsumerRequestSessionResult OpenResult { get; init; } =
            OpenConsumerRequestSessionResult.Success(Guid.NewGuid());
        public ReadResponseResult ReadResult { get; init; } = ReadResponseResult.NoResponse();

        public Task<OpenConsumerRequestSessionResult> OpenConsumerRequestSessionAsync(
            string channelId,
            string? listenerUrl,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken) =>
            Task.FromResult(OpenResult);

        public Task<CloseConsumerRequestSessionServiceResult> CloseConsumerRequestSessionAsync(string sessionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PostRequestResult> PostRequestAsync(
            string sessionId,
            IReadOnlyList<string?>? topics,
            InternalMessageContent messageContent,
            string? expiry,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ExpireRequestResult> ExpireRequestAsync(
            string sessionId,
            string messageId,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReadResponseResult> ReadResponseAsync(
            string sessionId,
            string requestMessageId,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken) =>
            Task.FromResult(ReadResult);

        public Task<RemoveResponseResult> RemoveResponseAsync(
            string sessionId,
            string requestMessageId,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoOpUsernameTokenParser : IUsernameTokenParser
    {
        public IReadOnlyList<UsernameTokenInput> Parse(IReadOnlyList<SecurityTokenRequest>? securityTokens) => [];

        public string NormalizeUsername(string username) => username;
    }
}
