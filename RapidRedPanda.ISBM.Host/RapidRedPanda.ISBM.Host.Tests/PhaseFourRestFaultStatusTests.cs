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
    public async Task ExpireRequest_ServiceSuccess_ReturnsNoContent()
    {
        ConsumerRequestService service = new();
        ConsumerRequestServiceController controller = CreateConsumerRequestController(service);

        IActionResult result = await controller.ExpireRequest(
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(1, service.ExpireCalls);
    }

    [Fact]
    public async Task ExpireRequest_UnauthorizedSession_DoesNotInvokeService()
    {
        ConsumerRequestService service = new();
        ConsumerRequestServiceController controller = CreateConsumerRequestController(
            service, new AuthorizationService { SessionOutcome = IsbmAuthorizationOutcome.NotFoundOrUnauthorized });

        IActionResult result = await controller.ExpireRequest(
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), CancellationToken.None);

        AssertFault<NotFoundObjectResult>(result, "SessionFault");
        Assert.Equal(0, service.ExpireCalls);
    }

    [Theory]
    [InlineData(ExpireRequestFailureKind.ParameterFault, 400, "ParameterFault")]
    [InlineData(ExpireRequestFailureKind.SessionFault, 404, "SessionFault")]
    [InlineData(ExpireRequestFailureKind.OperationFault, 500, "OperationFault")]
    public async Task ExpireRequest_ServiceFailure_PreservesFaultMapping(
        ExpireRequestFailureKind failure, int status, string fault)
    {
        ConsumerRequestServiceController controller = CreateConsumerRequestController(
            new ConsumerRequestService { ExpireResult = ExpireRequestResult.Failure(failure) });

        IActionResult result = await controller.ExpireRequest(
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), CancellationToken.None);

        ObjectResult response = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(fault, Assert.IsType<FaultResponse>(response.Value).Fault);
    }

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
    public async Task ReadPublication_WrongSessionKind_ReturnsUnprocessableOperationFault()
    {
        ConsumerPublicationServiceController controller = CreateConsumerPublicationController(
            new ConsumerPublicationService(),
            new AuthorizationService { SessionOutcome = IsbmAuthorizationOutcome.WrongSessionKind });

        IActionResult result = await controller.ReadPublication(Guid.NewGuid().ToString("D"), CancellationToken.None);

        AssertFault<UnprocessableEntityObjectResult>(result, "OperationFault");
    }

    [Fact]
    public async Task ReadRequest_WrongSessionKind_ReturnsUnprocessableOperationFault()
    {
        ProviderRequestServiceController controller = CreateProviderRequestController(
            new ProviderRequestService(),
            new AuthorizationService { SessionOutcome = IsbmAuthorizationOutcome.WrongSessionKind });

        IActionResult result = await controller.ReadRequest(Guid.NewGuid().ToString("D"), CancellationToken.None);

        AssertFault<UnprocessableEntityObjectResult>(result, "OperationFault");
    }

    [Fact]
    public async Task ReadResponse_WrongSessionKind_ReturnsUnprocessableOperationFault()
    {
        ConsumerRequestServiceController controller = CreateConsumerRequestController(
            new ConsumerRequestService(),
            new AuthorizationService { SessionOutcome = IsbmAuthorizationOutcome.WrongSessionKind });

        IActionResult result = await controller.ReadResponse(
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            CancellationToken.None);

        AssertFault<UnprocessableEntityObjectResult>(result, "OperationFault");
    }

    [Fact]
    public async Task PostPublication_WrongSessionKind_ReturnsUnprocessableOperationFault()
    {
        ProviderPublicationServiceController controller = CreateProviderPublicationController(
            new ProviderPublicationService(),
            new AuthorizationService { SessionOutcome = IsbmAuthorizationOutcome.WrongSessionKind });
        SetJsonBody(controller, """{"messageContent":{"content":{"reading":42}}}""");

        IActionResult result = await controller.PostPublication(Guid.NewGuid().ToString("D"), CancellationToken.None);

        AssertFault<UnprocessableEntityObjectResult>(result, "OperationFault");
    }

    [Fact]
    public async Task Authorization_WrongRequestedSessionKind_ReturnsWrongSessionKind()
    {
        Guid sessionId = Guid.NewGuid();
        IsbmOperationAuthorizationService authorization = CreateAuthorizationService(
            subscriptions: new SubscriptionSessionRepository(),
            publications: new ProviderPublicationSessionRepository(),
            providerRequests: new ProviderRequestSessionRepository
            {
                GetResult = GetProviderRequestSessionResult.Success(CreateProviderRequestSession(sessionId))
            },
            consumerRequests: new ConsumerRequestSessionRepository());

        IsbmAuthorizationOutcome result = await authorization.AuthorizeSessionAsync(
            sessionId.ToString("D"),
            IsbmSessionKind.ConsumerPublication,
            credentials: null,
            CancellationToken.None);

        Assert.Equal(IsbmAuthorizationOutcome.WrongSessionKind, result);
    }

    [Fact]
    public async Task Authorization_WrongSessionProbeDatabaseFailure_ReturnsFailure()
    {
        Guid sessionId = Guid.NewGuid();
        IsbmOperationAuthorizationService authorization = CreateAuthorizationService(
            subscriptions: new SubscriptionSessionRepository(),
            publications: new ProviderPublicationSessionRepository
            {
                GetResult = GetProviderPublicationSessionResult.Failure(
                    GetProviderPublicationSessionFailureKind.DatabaseFailure,
                    "database unavailable")
            },
            providerRequests: new ProviderRequestSessionRepository(),
            consumerRequests: new ConsumerRequestSessionRepository());

        IsbmAuthorizationOutcome result = await authorization.AuthorizeSessionAsync(
            sessionId.ToString("D"),
            IsbmSessionKind.ConsumerPublication,
            credentials: null,
            CancellationToken.None);

        Assert.Equal(IsbmAuthorizationOutcome.Failure, result);
    }

    [Fact]
    public async Task Authorization_AnySession_IgnoresWrongKindProbeUntilRequestedKindAuthorizes()
    {
        Guid sessionId = Guid.NewGuid();
        IsbmOperationAuthorizationService authorization = CreateAuthorizationService(
            subscriptions: new SubscriptionSessionRepository(),
            publications: new ProviderPublicationSessionRepository(),
            providerRequests: new ProviderRequestSessionRepository
            {
                GetResult = GetProviderRequestSessionResult.Success(CreateProviderRequestSession(sessionId))
            },
            consumerRequests: new ConsumerRequestSessionRepository());

        IsbmAuthorizationOutcome result = await authorization.AuthorizeAnySessionAsync(
            sessionId.ToString("D"),
            credentials: null,
            CancellationToken.None);

        Assert.Equal(IsbmAuthorizationOutcome.Authorized, result);
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

    private static ProviderPublicationServiceController CreateProviderPublicationController(
        ProviderPublicationService service,
        IIsbmOperationAuthorizationService? authorization = null)
    {
        ProviderPublicationServiceController controller = new(
            service,
            new NoOpUsernameTokenParser(),
            NullLogger<ProviderPublicationServiceController>.Instance,
            authorization);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static ConsumerPublicationServiceController CreateConsumerPublicationController(
        ConsumerPublicationService service,
        IIsbmOperationAuthorizationService? authorization = null)
    {
        ConsumerPublicationServiceController controller = new(
            service,
            new NoOpUsernameTokenParser(),
            NullLogger<ConsumerPublicationServiceController>.Instance,
            authorization);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static ProviderRequestServiceController CreateProviderRequestController(
        ProviderRequestService service,
        IIsbmOperationAuthorizationService? authorization = null)
    {
        ProviderRequestServiceController controller = new(
            service,
            new NoOpUsernameTokenParser(),
            NullLogger<ProviderRequestServiceController>.Instance,
            authorization);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static ConsumerRequestServiceController CreateConsumerRequestController(
        ConsumerRequestService service,
        IIsbmOperationAuthorizationService? authorization = null)
    {
        ConsumerRequestServiceController controller = new(
            service,
            new NoOpUsernameTokenParser(),
            NullLogger<ConsumerRequestServiceController>.Instance,
            authorization);
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

    private static IsbmOperationAuthorizationService CreateAuthorizationService(
        ISubscriptionSessionRepository subscriptions,
        IProviderPublicationSessionRepository publications,
        IProviderRequestSessionRepository providerRequests,
        IConsumerRequestSessionRepository consumerRequests) =>
        new(
            new ChannelRepository(),
            subscriptions,
            publications,
            providerRequests,
            consumerRequests);

    private static ProviderRequestSessionData CreateProviderRequestSession(Guid sessionId) =>
        new(
            sessionId,
            Guid.NewGuid(),
            ProviderPublicationSessionStates.Open,
            "topic",
            "subscription",
            ["topic"],
            AuthenticatedTokenUsername: null,
            DateTime.UtcNow,
            ClosedUtc: null);

    private sealed class AuthorizationService : IIsbmOperationAuthorizationService
    {
        public IsbmAuthorizationOutcome SessionOutcome { get; init; } = IsbmAuthorizationOutcome.Authorized;

        public Task<IsbmAuthorizationOutcome> AuthorizeChannelAsync(
            string channelUri,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IsbmAuthorizationOutcome> AuthorizeSessionAsync(
            string sessionId,
            IsbmSessionKind kind,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken) =>
            Task.FromResult(SessionOutcome);

        public Task<IsbmAuthorizationOutcome> AuthorizeAnySessionAsync(
            string sessionId,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ChannelRepository : IChannelRepository
    {
        public Task<IReadOnlyList<ChannelData>> GetChannelsAsync(ChannelCredentials? credentials, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GetChannelResult> GetChannelAsync(string channelUri, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GetChannelTopicBindingResult> GetChannelTopicBindingAsync(string channelUri, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GetChannelTopicBindingResult> GetChannelTopicBindingByIdAsync(Guid channelId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GetChannelForSubscriptionResult> GetChannelForConsumerPublicationSubscriptionAsync(
            string channelUri,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GetChannelForSubscriptionResult> GetChannelForProviderPublicationSessionAsync(
            string channelUri,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ValidateChannelCredentialsResult> ValidateChannelCredentialsAsync(
            Guid channelId,
            string? expectedAuthenticatedTokenUsername,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken) =>
            Task.FromResult(ValidateChannelCredentialsResult.Success());

        public Task<CreateChannelResult> CreateChannelAsync(ChannelCreateRequestData request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DeleteChannelResult> DeleteChannelAsync(string channelUri, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SecurityTokenMutationResult> AddSecurityTokensAsync(
            string channelUri,
            IReadOnlyList<ChannelCreateToken> tokens,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SecurityTokenMutationResult> RemoveSecurityTokensAsync(
            string channelUri,
            IReadOnlyList<ChannelCreateToken> tokens,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class SubscriptionSessionRepository : ISubscriptionSessionRepository
    {
        public GetSubscriptionSessionResult GetResult { get; init; } =
            GetSubscriptionSessionResult.Failure(GetSubscriptionSessionFailureKind.NotFound, "missing");

        public Task<InsertSubscriptionSessionResult> InsertConsumerSubscriptionSessionAsync(
            ConsumerSubscriptionSessionCreateData session,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GetSubscriptionSessionResult> GetSubscriptionSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(GetResult);

        public Task<CloseSubscriptionSessionResult> CloseSubscriptionSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ProviderPublicationSessionRepository : IProviderPublicationSessionRepository
    {
        public GetProviderPublicationSessionResult GetResult { get; init; } =
            GetProviderPublicationSessionResult.Failure(GetProviderPublicationSessionFailureKind.NotFound, "missing");

        public Task<InsertProviderPublicationSessionResult> InsertProviderPublicationSessionAsync(
            ProviderPublicationSessionCreateData session,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GetProviderPublicationSessionResult> GetProviderPublicationSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(GetResult);

        public Task<CloseProviderPublicationSessionResult> CloseProviderPublicationSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ProviderRequestSessionRepository : IProviderRequestSessionRepository
    {
        public GetProviderRequestSessionResult GetResult { get; init; } =
            GetProviderRequestSessionResult.Failure(GetProviderRequestSessionFailureKind.NotFound, "missing");

        public Task<InsertProviderRequestSessionResult> InsertProviderRequestSessionAsync(
            ProviderRequestSessionCreateData session,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GetProviderRequestSessionResult> GetProviderRequestSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(GetResult);

        public Task<CloseProviderRequestSessionRepositoryResult> CloseProviderRequestSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ConsumerRequestSessionRepository : IConsumerRequestSessionRepository
    {
        public GetConsumerRequestSessionResult GetResult { get; init; } =
            GetConsumerRequestSessionResult.Failure(GetConsumerRequestSessionFailureKind.NotFound, "missing");

        public Task<InsertConsumerRequestSessionResult> InsertConsumerRequestSessionAsync(
            ConsumerRequestSessionCreateData session,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GetConsumerRequestSessionResult> GetConsumerRequestSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(GetResult);

        public Task<CloseConsumerRequestSessionResult> CloseConsumerRequestSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
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
        public ExpireRequestResult ExpireResult { get; init; } = ExpireRequestResult.Success();
        public int ExpireCalls { get; private set; }

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
            CancellationToken cancellationToken)
        {
            ExpireCalls++;
            return Task.FromResult(ExpireResult);
        }

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
