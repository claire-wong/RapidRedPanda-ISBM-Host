using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRedPanda.ISBM.Host.Contracts;
using RapidRedPanda.ISBM.Host.Controllers;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Services;
using System.Reflection;
using System.Text.Json;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class ConsumerPublicationServiceControllerTests
{
    [Fact]
    public void ConsumerPublicationController_DoesNotExposeSharedSessionCloseRoute()
    {
        string[] deleteRoutes = GetAllRoutes<HttpDeleteAttribute>();

        Assert.DoesNotContain("isbm/2.0/sessions/{sessionId}", deleteRoutes);
        Assert.DoesNotContain("isbm/2.0/subscription-sessions/{sessionId}", deleteRoutes);
        Assert.DoesNotContain("isbm/2.0/publication-sessions/{sessionId}", deleteRoutes);
    }

    [Fact]
    public void OpenSubscriptionSession_RouteMetadata_RemainsUnchanged()
    {
        RouteAttribute controllerRoute = Assert.IsType<RouteAttribute>(
            typeof(ConsumerPublicationServiceController).GetCustomAttribute<RouteAttribute>());
        MethodInfo openAction = GetAction(nameof(ConsumerPublicationServiceController.OpenSubscriptionSession));
        HttpPostAttribute postAttribute = Assert.Single(openAction.GetCustomAttributes<HttpPostAttribute>());

        Assert.Equal("isbm/2.0/channels/{channelId}/subscription-sessions", CombineRoutes(controllerRoute.Template, postAttribute.Template));
    }

    [Fact]
    public void ReadPublication_RouteMetadata_ExposesSingularSessionPublicationGetRoute()
    {
        string[] routes = GetRoutes<HttpGetAttribute>(nameof(ConsumerPublicationServiceController.ReadPublication));

        Assert.Equal(["isbm/2.0/sessions/{sessionId}/publication"], routes);
        Assert.DoesNotContain("isbm/2.0/sessions/{sessionId}/publications", routes);
    }

    [Fact]
    public void RemovePublication_RouteMetadata_ExposesSingularSessionPublicationDeleteRoute()
    {
        string[] routes = GetRoutes<HttpDeleteAttribute>(nameof(ConsumerPublicationServiceController.RemovePublication));

        Assert.Contains("isbm/2.0/sessions/{sessionId}/publication", routes);
        Assert.DoesNotContain("isbm/2.0/sessions/{sessionId}/publications", routes);
    }

    [Fact]
    public void SwaggerEndpointDiscovery_ReadAndRemovePublicationRoutesAreAvailable()
    {
        Assert.Equal(["isbm/2.0/sessions/{sessionId}/publication"], GetRoutes<HttpGetAttribute>(
            nameof(ConsumerPublicationServiceController.ReadPublication)));
        Assert.Equal(["isbm/2.0/sessions/{sessionId}/publication"], GetRoutes<HttpDeleteAttribute>(
            nameof(ConsumerPublicationServiceController.RemovePublication)));
    }

    [Fact]
    public void EndpointMetadata_ConsumerPublicationRoutesHaveNoAmbiguousGetPluralAlias()
    {
        string[] getRoutes = GetAllRoutes<HttpGetAttribute>();
        string[] deleteRoutes = GetAllRoutes<HttpDeleteAttribute>();

        Assert.Equal(1, getRoutes.Count(route => route == "isbm/2.0/sessions/{sessionId}/publication"));
        Assert.DoesNotContain("isbm/2.0/sessions/{sessionId}/publications", getRoutes);
        Assert.Contains("isbm/2.0/sessions/{sessionId}/publication", deleteRoutes);
    }

    [Fact]
    public async Task OpenSubscriptionSession_WithListenerUrl_DelegatesExactValue()
    {
        const string listenerUrl = "https://listener.example.com/isbm?tenant=a";
        CapturingSubscriptionSessionService service = new();
        ConsumerPublicationServiceController controller = CreateController(service);
        SetJsonBody(controller, $$"""{"topics":["Topic-A"],"listenerUrl":"{{listenerUrl}}"}""");

        IActionResult result = await controller.OpenSubscriptionSession("%2Fpublication%2Fchannel", CancellationToken.None);

        ObjectResult created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(listenerUrl, service.OpenedListenerUrl);
        Assert.Equal(["Topic-A"], service.OpenedTopics);
    }

    [Fact]
    public async Task OpenSubscriptionSession_WithoutListenerUrl_DelegatesNull()
    {
        CapturingSubscriptionSessionService service = new();
        ConsumerPublicationServiceController controller = CreateController(service);
        SetJsonBody(controller, """{"topics":["Topic-A"]}""");

        IActionResult result = await controller.OpenSubscriptionSession("%2Fpublication%2Fchannel", CancellationToken.None);

        ObjectResult created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Null(service.OpenedListenerUrl);
        Assert.Equal(["Topic-A"], service.OpenedTopics);
    }

    [Fact]
    public async Task ReadPublication_PublicationAvailable_ReturnsOkPublicationResponse()
    {
        Guid publicationId = Guid.NewGuid();
        CapturingSubscriptionSessionService service = new()
        {
            ReadResult = ReadPublicationResult.Success(new ReadPublicationData(
                publicationId,
                ["Topic-A", "Topic-B"],
                """{"content":{"reading":42}}"""))
        };
        ConsumerPublicationServiceController controller = CreateController(service);

        IActionResult result = await controller.ReadPublication(Guid.NewGuid().ToString(), CancellationToken.None);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        ReadPublicationResponse response = Assert.IsType<ReadPublicationResponse>(ok.Value);
        Assert.Equal(publicationId, response.MessageId);
        Assert.Equal(["Topic-A", "Topic-B"], response.Topics);
        Assert.False(response.MessageContent.TryGetProperty("mediaType", out _));
        Assert.True(response.MessageContent.TryGetProperty("content", out JsonElement content));
        Assert.Equal(42, content.GetProperty("reading").GetInt32());
        Assert.Equal(JsonValueKind.Object, content.ValueKind);
    }

    [Fact]
    public async Task ReadPublication_NoPublicationAvailable_ReturnsNoContent()
    {
        ConsumerPublicationServiceController controller = CreateController(new CapturingSubscriptionSessionService
        {
            ReadResult = ReadPublicationResult.NoPublication()
        });

        IActionResult result = await controller.ReadPublication(Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task ReadPublication_InvalidSessionUuid_ReturnsParameterFault()
    {
        ConsumerPublicationServiceController controller = CreateController(new CapturingSubscriptionSessionService
        {
            ReadResult = ReadPublicationResult.Failure(ReadPublicationFailureKind.ParameterFault)
        });

        IActionResult result = await controller.ReadPublication("not-a-guid", CancellationToken.None);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(badRequest.Value);
        Assert.Equal("ParameterFault", fault.Fault);
    }

    [Fact]
    public async Task ReadPublication_MissingSession_ReturnsSessionFault()
    {
        ConsumerPublicationServiceController controller = CreateController(new CapturingSubscriptionSessionService
        {
            ReadResult = ReadPublicationResult.Failure(ReadPublicationFailureKind.SessionFault)
        });

        IActionResult result = await controller.ReadPublication(Guid.NewGuid().ToString(), CancellationToken.None);

        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(notFound.Value);
        Assert.Equal("SessionFault", fault.Fault);
    }

    [Fact]
    public async Task RemovePublication_ValidSession_ReturnsNoContent()
    {
        ConsumerPublicationServiceController controller = CreateController(new CapturingSubscriptionSessionService
        {
            RemoveResult = RemovePublicationResult.Success()
        });

        IActionResult result = await controller.RemovePublication(Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task RemovePublication_InvalidSessionUuid_ReturnsParameterFault()
    {
        ConsumerPublicationServiceController controller = CreateController(new CapturingSubscriptionSessionService
        {
            RemoveResult = RemovePublicationResult.Failure(RemovePublicationFailureKind.ParameterFault)
        });

        IActionResult result = await controller.RemovePublication("not-a-guid", CancellationToken.None);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(badRequest.Value);
        Assert.Equal("ParameterFault", fault.Fault);
    }

    [Fact]
    public async Task RemovePublication_MissingSession_ReturnsSessionFault()
    {
        ConsumerPublicationServiceController controller = CreateController(new CapturingSubscriptionSessionService
        {
            RemoveResult = RemovePublicationResult.Failure(RemovePublicationFailureKind.SessionFault)
        });

        IActionResult result = await controller.RemovePublication(Guid.NewGuid().ToString(), CancellationToken.None);

        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(notFound.Value);
        Assert.Equal("SessionFault", fault.Fault);
    }

    private static ConsumerPublicationServiceController CreateController(CapturingSubscriptionSessionService service)
    {
        ConsumerPublicationServiceController controller = new(
            service,
            new NoOpUsernameTokenParser(),
            NullLogger<ConsumerPublicationServiceController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private static void SetJsonBody(Controller controller, string json)
    {
        controller.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        controller.Request.ContentType = "application/json";
    }

    private static string[] GetAllRoutes<TAttribute>()
        where TAttribute : HttpMethodAttribute
    {
        RouteAttribute controllerRoute = Assert.IsType<RouteAttribute>(
            typeof(ConsumerPublicationServiceController).GetCustomAttribute<RouteAttribute>());

        return typeof(ConsumerPublicationServiceController)
            .GetMethods()
            .SelectMany(method => method.GetCustomAttributes<TAttribute>())
            .Select(attribute => CombineRoutes(controllerRoute.Template, attribute.Template))
            .ToArray();
    }

    private static string[] GetRoutes<TAttribute>(string actionName)
        where TAttribute : HttpMethodAttribute
    {
        RouteAttribute controllerRoute = Assert.IsType<RouteAttribute>(
            typeof(ConsumerPublicationServiceController).GetCustomAttribute<RouteAttribute>());
        MethodInfo action = GetAction(actionName);

        return action
            .GetCustomAttributes<TAttribute>()
            .Select(attribute => CombineRoutes(controllerRoute.Template, attribute.Template))
            .ToArray();
    }

    private static MethodInfo GetAction(string name)
    {
        MethodInfo? method = typeof(ConsumerPublicationServiceController).GetMethod(name);
        Assert.NotNull(method);
        return method;
    }

    private static string CombineRoutes(string? prefix, string? template)
    {
        return string.Join(
            '/',
            new[] { prefix, template }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim('/')));
    }

    private sealed class CapturingSubscriptionSessionService : IConsumerSubscriptionSessionService
    {
        public CloseConsumerSubscriptionSessionResult CloseResult { get; init; } = CloseConsumerSubscriptionSessionResult.Success();
        public ReadPublicationResult ReadResult { get; init; } = ReadPublicationResult.NoPublication();
        public RemovePublicationResult RemoveResult { get; init; } = RemovePublicationResult.Success();

        public string? ClosedSessionId { get; private set; }
        public IReadOnlyList<string>? OpenedTopics { get; private set; }
        public string? OpenedListenerUrl { get; private set; }
        public string? OpenedFilterExpressionsJson { get; private set; }

        public Task<OpenSubscriptionSessionResult> OpenSubscriptionSessionAsync(
            string channelId,
            IReadOnlyList<string> topics,
            string? listenerUrl,
            string? filterExpressionsJson,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            OpenedTopics = topics;
            OpenedListenerUrl = listenerUrl;
            OpenedFilterExpressionsJson = filterExpressionsJson;
            return Task.FromResult(OpenSubscriptionSessionResult.Success(Guid.NewGuid()));
        }

        public Task<CloseConsumerSubscriptionSessionResult> CloseSubscriptionSessionAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            ClosedSessionId = sessionId;
            return Task.FromResult(CloseResult);
        }

        public Task<ReadPublicationResult> ReadPublicationAsync(string sessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(ReadResult);
        }

        public Task<RemovePublicationResult> RemovePublicationAsync(string sessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(RemoveResult);
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
