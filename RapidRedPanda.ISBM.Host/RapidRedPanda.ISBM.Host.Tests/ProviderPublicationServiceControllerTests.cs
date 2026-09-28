using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRedPanda.ISBM.Host.Contracts;
using RapidRedPanda.ISBM.Host.Controllers;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Messaging;
using RapidRedPanda.ISBM.Host.Services;
using System.Reflection;
using System.Text;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class ProviderPublicationServiceControllerTests
{
    [Fact]
    public void OpenPublicationSession_RouteMetadata_ExposesCanonicalRoute()
    {
        Assert.Equal(["isbm/2.0/channels/{channelId}/publication-sessions"], GetRoutes<HttpPostAttribute>(
            nameof(ProviderPublicationServiceController.OpenPublicationSession)));
    }

    [Fact]
    public void PostPublication_RouteMetadata_ExposesCanonicalProviderPublicationRoute()
    {
        Assert.Equal(["isbm/2.0/sessions/{sessionId}/publications"], GetRoutes<HttpPostAttribute>(
            nameof(ProviderPublicationServiceController.PostPublication)));
    }

    [Fact]
    public void ProviderController_DoesNotExposePublicationSessionCloseRoute()
    {
        string[] deleteRoutes = typeof(ProviderPublicationServiceController)
            .GetMethods()
            .SelectMany(method => method.GetCustomAttributes<HttpDeleteAttribute>())
            .Select(attribute => CombineRoutes("isbm/2.0", attribute.Template))
            .ToArray();

        Assert.DoesNotContain("isbm/2.0/publication-sessions/{sessionId}", deleteRoutes);
        Assert.DoesNotContain("isbm/2.0/sessions/{sessionId}", deleteRoutes);
    }

    [Fact]
    public async Task OpenPublicationSession_ValidChannel_ReturnsCreatedWithSessionId()
    {
        Guid sessionId = Guid.NewGuid();
        ProviderPublicationServiceController controller = CreateController(new CapturingProviderPublicationSessionService
        {
            OpenResult = OpenPublicationSessionResult.Success(sessionId)
        });

        IActionResult result = await controller.OpenPublicationSession("%2Fpublication%2Fchannel", CancellationToken.None);

        ObjectResult created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        OpenPublicationSessionResponse response = Assert.IsType<OpenPublicationSessionResponse>(created.Value);
        Assert.Equal(sessionId, response.SessionId);
    }

    [Fact]
    public async Task OpenPublicationSession_InvalidChannelId_ReturnsParameterFault()
    {
        ProviderPublicationServiceController controller = CreateController(new CapturingProviderPublicationSessionService
        {
            OpenResult = OpenPublicationSessionResult.Failure(OpenPublicationSessionFailureKind.ParameterFault)
        });

        IActionResult result = await controller.OpenPublicationSession("%20%2Fbad", CancellationToken.None);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(badRequest.Value);
        Assert.Equal("ParameterFault", fault.Fault);
    }

    [Fact]
    public async Task OpenPublicationSession_MissingChannel_ReturnsChannelFault()
    {
        ProviderPublicationServiceController controller = CreateController(new CapturingProviderPublicationSessionService
        {
            OpenResult = OpenPublicationSessionResult.Failure(OpenPublicationSessionFailureKind.ChannelFault)
        });

        IActionResult result = await controller.OpenPublicationSession("%2Fmissing", CancellationToken.None);

        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(notFound.Value);
        Assert.Equal("ChannelFault", fault.Fault);
    }

    [Fact]
    public async Task OpenPublicationSession_InvalidSecurityToken_ReturnsChannelFault()
    {
        ProviderPublicationServiceController controller = CreateController(new CapturingProviderPublicationSessionService
        {
            OpenResult = OpenPublicationSessionResult.Failure(OpenPublicationSessionFailureKind.SecurityTokenFault)
        });

        IActionResult result = await controller.OpenPublicationSession("%2Fsecured", CancellationToken.None);

        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(notFound.Value);
        Assert.Equal("ChannelFault", fault.Fault);
    }

    [Fact]
    public async Task OpenPublicationSession_RepositoryFailure_ReturnsOperationFault()
    {
        ProviderPublicationServiceController controller = CreateController(new CapturingProviderPublicationSessionService
        {
            OpenResult = OpenPublicationSessionResult.Failure(OpenPublicationSessionFailureKind.OperationFault)
        });

        IActionResult result = await controller.OpenPublicationSession("%2Fchannel", CancellationToken.None);

        ObjectResult operationFault = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, operationFault.StatusCode);
        FaultResponse fault = Assert.IsType<FaultResponse>(operationFault.Value);
        Assert.Equal("OperationFault", fault.Fault);
    }

    [Fact]
    public async Task PostPublication_ValidRequest_ReturnsCreatedWithMessageIdAndDelegatesMessageContent()
    {
        Guid publicationId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        CapturingProviderPublicationSessionService service = new()
        {
            PostResult = PostPublicationResult.Success(publicationId)
        };
        ProviderPublicationServiceController controller = CreateController(service);
        SetJsonBody(
            controller,
            """
            {
              "messageContent": { "content": { "reading": 42 } },
              "topics": ["Topic-A", "Topic-B"],
              "expiry": "PT1H"
            }
            """);

        IActionResult result = await controller.PostPublication(sessionId.ToString(), CancellationToken.None);

        ObjectResult created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        PostPublicationResponse response = Assert.IsType<PostPublicationResponse>(created.Value);
        Assert.Equal(publicationId, response.MessageId);
        Assert.Equal(sessionId.ToString(), service.PostedSessionId);
        Assert.Equal(["Topic-A", "Topic-B"], service.PostedTopics);
        Assert.Equal(InternalMessageContentKind.Json, service.PostedMessageContent?.Kind);
        Assert.Equal("""{ "reading": 42 }""", service.PostedMessageContent?.Payload.ToString());
        Assert.Null(service.PostedMessageContent?.MediaType);
        Assert.Equal("PT1H", service.PostedExpiry);
    }

    [Fact]
    public async Task PostPublication_MalformedJson_ReturnsParameterFaultWithoutDelegating()
    {
        CapturingProviderPublicationSessionService service = new();
        ProviderPublicationServiceController controller = CreateController(service);
        SetJsonBody(controller, """{"topics": [""");

        IActionResult result = await controller.PostPublication(Guid.NewGuid().ToString(), CancellationToken.None);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(badRequest.Value);
        Assert.Equal("ParameterFault", fault.Fault);
        Assert.Null(service.PostedSessionId);
    }

    [Fact]
    public async Task PostPublication_MissingMessageContent_ReturnsParameterFaultWithoutDelegating()
    {
        CapturingProviderPublicationSessionService service = new();
        ProviderPublicationServiceController controller = CreateController(service);
        SetJsonBody(controller, """{"topics":["Topic-A"],"expiry":"PT1H"}""");

        IActionResult result = await controller.PostPublication(Guid.NewGuid().ToString(), CancellationToken.None);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(badRequest.Value);
        Assert.Equal("ParameterFault", fault.Fault);
        Assert.Null(service.PostedSessionId);
    }

    [Fact]
    public async Task PostPublication_MissingExpiry_DelegatesNullExpiry()
    {
        CapturingProviderPublicationSessionService service = new();
        ProviderPublicationServiceController controller = CreateController(service);
        SetJsonBody(
            controller,
            """{"messageContent":{"content":{"reading":42}},"topics":["Topic-A"]}""");

        IActionResult result = await controller.PostPublication(Guid.NewGuid().ToString(), CancellationToken.None);

        ObjectResult created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Null(service.PostedExpiry);
    }

    [Fact]
    public async Task PostPublication_MessageContentWithoutContent_ReturnsParameterFaultWithoutDelegating()
    {
        CapturingProviderPublicationSessionService service = new();
        ProviderPublicationServiceController controller = CreateController(service);
        SetJsonBody(controller, """{"messageContent":{"mediaType":"application/xml"},"topics":["Topic-A"]}""");

        IActionResult result = await controller.PostPublication(Guid.NewGuid().ToString(), CancellationToken.None);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(badRequest.Value);
        Assert.Equal("ParameterFault", fault.Fault);
        Assert.Null(service.PostedSessionId);
    }

    [Theory]
    [InlineData(PostPublicationFailureKind.ParameterFault, typeof(BadRequestObjectResult), "ParameterFault")]
    [InlineData(PostPublicationFailureKind.SessionFault, typeof(NotFoundObjectResult), "SessionFault")]
    [InlineData(PostPublicationFailureKind.ChannelFault, typeof(NotFoundObjectResult), "ChannelFault")]
    [InlineData(PostPublicationFailureKind.OperationFault, typeof(ObjectResult), "OperationFault")]
    public async Task PostPublication_FailureKind_MapsToExpectedFault(
        PostPublicationFailureKind failureKind,
        Type resultType,
        string expectedFault)
    {
        ProviderPublicationServiceController controller = CreateController(new CapturingProviderPublicationSessionService
        {
            PostResult = PostPublicationResult.Failure(failureKind)
        });
        SetJsonBody(
            controller,
            """{"topics":["Topic-A"],"messageContent":{"content":{"reading":42}},"expiry":"PT1H"}""");

        IActionResult result = await controller.PostPublication(Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.IsType(resultType, result);
        ObjectResult objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(objectResult.Value);
        Assert.Equal(expectedFault, fault.Fault);
        if (failureKind == PostPublicationFailureKind.OperationFault)
        {
            Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
        }
    }

    private static ProviderPublicationServiceController CreateController(CapturingProviderPublicationSessionService service)
    {
        ProviderPublicationServiceController controller = new(
            service,
            new NoOpUsernameTokenParser(),
            NullLogger<ProviderPublicationServiceController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private static void SetJsonBody(ProviderPublicationServiceController controller, string json)
    {
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        controller.Request.ContentType = "application/json";
    }

    private static string[] GetRoutes<TAttribute>(string actionName)
        where TAttribute : HttpMethodAttribute
    {
        RouteAttribute controllerRoute = Assert.IsType<RouteAttribute>(
            typeof(ProviderPublicationServiceController).GetCustomAttribute<RouteAttribute>());
        MethodInfo action = Assert.Single(
            typeof(ProviderPublicationServiceController).GetMethods(),
            method => method.Name == actionName);

        return action
            .GetCustomAttributes<TAttribute>()
            .Select(attribute => CombineRoutes(controllerRoute.Template, attribute.Template))
            .ToArray();
    }

    private static string CombineRoutes(string? prefix, string? template)
    {
        return string.Join(
            '/',
            new[] { prefix, template }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim('/')));
    }

    private sealed class CapturingProviderPublicationSessionService : IProviderPublicationSessionService
    {
        public OpenPublicationSessionResult OpenResult { get; init; } = OpenPublicationSessionResult.Success(Guid.NewGuid());
        public PostPublicationResult PostResult { get; init; } = PostPublicationResult.Success(Guid.NewGuid());
        public string? PostedSessionId { get; private set; }
        public IReadOnlyList<string?>? PostedTopics { get; private set; }
        public InternalMessageContent? PostedMessageContent { get; private set; }
        public string? PostedExpiry { get; private set; }

        public Task<OpenPublicationSessionResult> OpenPublicationSessionAsync(
            string channelId,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(OpenResult);
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
            PostedSessionId = sessionId;
            PostedTopics = topics;
            PostedMessageContent = messageContent;
            PostedExpiry = expiry;
            return Task.FromResult(PostResult);
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
