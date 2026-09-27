using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRedPanda.ISBM.Host.Contracts;
using RapidRedPanda.ISBM.Host.Controllers;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Services;
using System.Reflection;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class SessionServiceControllerTests
{
    [Fact]
    public void CloseSession_RouteMetadata_ExposesCanonicalSharedRoute()
    {
        Assert.Equal(["isbm/2.0/sessions/{sessionId}"], GetRoutes<HttpDeleteAttribute>(
            nameof(SessionServiceController.CloseSession)));
    }

    [Fact]
    public void CloseSession_RouteMetadata_DoesNotExposeProviderSpecificOrLegacyAlias()
    {
        string[] routes = GetRoutes<HttpDeleteAttribute>(nameof(SessionServiceController.CloseSession));

        Assert.DoesNotContain("isbm/2.0/publication-sessions/{sessionId}", routes);
        Assert.DoesNotContain("isbm/2.0/subscription-sessions/{sessionId}", routes);
    }

    [Fact]
    public void EndpointMetadata_OnlyOneSharedSessionCloseRouteExists()
    {
        string[] routes = GetDeleteRoutesFor(
            typeof(SessionServiceController),
            typeof(ConsumerPublicationServiceController),
            typeof(ProviderPublicationServiceController));

        Assert.Equal(1, routes.Count(route => route == "isbm/2.0/sessions/{sessionId}"));
        Assert.DoesNotContain("isbm/2.0/publication-sessions/{sessionId}", routes);
    }

    [Fact]
    public async Task CloseSession_ValidSession_ReturnsNoContent()
    {
        CapturingSessionCloseService closeService = new()
        {
            CloseResult = CloseSessionResult.Success()
        };
        SessionServiceController controller = CreateController(closeService);
        string sessionId = Guid.NewGuid().ToString();

        IActionResult result = await controller.CloseSession(sessionId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(sessionId, closeService.ClosedSessionId);
    }

    [Fact]
    public async Task CloseSession_InvalidSessionUuid_ReturnsParameterFault()
    {
        SessionServiceController controller = CreateController(new CapturingSessionCloseService
        {
            CloseResult = CloseSessionResult.Failure(CloseSessionFailureKind.ParameterFault)
        });

        IActionResult result = await controller.CloseSession("not-a-guid", CancellationToken.None);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(badRequest.Value);
        Assert.Equal("ParameterFault", fault.Fault);
    }

    [Fact]
    public async Task CloseSession_MissingOrAlreadyClosedSession_ReturnsSessionFault()
    {
        SessionServiceController controller = CreateController(new CapturingSessionCloseService
        {
            CloseResult = CloseSessionResult.Failure(CloseSessionFailureKind.SessionFault)
        });

        IActionResult result = await controller.CloseSession(Guid.NewGuid().ToString(), CancellationToken.None);

        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(notFound.Value);
        Assert.Equal("SessionFault", fault.Fault);
    }

    [Fact]
    public async Task CloseSession_OperationFailure_ReturnsOperationFault()
    {
        SessionServiceController controller = CreateController(new CapturingSessionCloseService
        {
            CloseResult = CloseSessionResult.Failure(CloseSessionFailureKind.OperationFault)
        });

        IActionResult result = await controller.CloseSession(Guid.NewGuid().ToString(), CancellationToken.None);

        ObjectResult operationFault = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, operationFault.StatusCode);
        FaultResponse fault = Assert.IsType<FaultResponse>(operationFault.Value);
        Assert.Equal("OperationFault", fault.Fault);
    }

    [Fact]
    public async Task OpenProviderPublicationSessionThenCloseSharedSessionRoute_UsesExactReturnedSessionId()
    {
        Guid providerSessionId = Guid.NewGuid();
        ProviderPublicationServiceController providerController = new(
            new CapturingProviderPublicationSessionService
            {
                OpenResult = OpenPublicationSessionResult.Success(providerSessionId)
            },
            new NoOpUsernameTokenParser(),
            NullLogger<ProviderPublicationServiceController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        CapturingSessionCloseService closeService = new()
        {
            CloseResult = CloseSessionResult.Success()
        };
        SessionServiceController sharedSessionController = CreateController(closeService);

        IActionResult openResult = await providerController.OpenPublicationSession("%2Fpublication%2Fchannel", CancellationToken.None);

        ObjectResult created = Assert.IsType<ObjectResult>(openResult);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        OpenPublicationSessionResponse response = Assert.IsType<OpenPublicationSessionResponse>(created.Value);

        IActionResult closeResult = await sharedSessionController.CloseSession(response.SessionId.ToString(), CancellationToken.None);

        Assert.IsType<NoContentResult>(closeResult);
        Assert.Equal(providerSessionId, response.SessionId);
        Assert.Equal(providerSessionId.ToString(), closeService.ClosedSessionId);
    }

    private static SessionServiceController CreateController(CapturingSessionCloseService closeService)
    {
        SessionServiceController controller = new(
            closeService,
            NullLogger<SessionServiceController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private static string[] GetRoutes<TAttribute>(string actionName)
        where TAttribute : HttpMethodAttribute
    {
        RouteAttribute controllerRoute = Assert.IsType<RouteAttribute>(
            typeof(SessionServiceController).GetCustomAttribute<RouteAttribute>());
        MethodInfo action = Assert.Single(
            typeof(SessionServiceController).GetMethods(),
            method => method.Name == actionName);

        return action
            .GetCustomAttributes<TAttribute>()
            .Select(attribute => CombineRoutes(controllerRoute.Template, attribute.Template))
            .ToArray();
    }

    private static string[] GetDeleteRoutesFor(params Type[] controllerTypes)
    {
        return controllerTypes
            .SelectMany(controllerType =>
            {
                RouteAttribute controllerRoute = Assert.IsType<RouteAttribute>(
                    controllerType.GetCustomAttribute<RouteAttribute>());
                return controllerType
                    .GetMethods()
                    .SelectMany(method => method.GetCustomAttributes<HttpDeleteAttribute>())
                    .Select(attribute => CombineRoutes(controllerRoute.Template, attribute.Template));
            })
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

    private sealed class CapturingSessionCloseService : ISessionCloseService
    {
        public CloseSessionResult CloseResult { get; init; } = CloseSessionResult.Success();
        public string? ClosedSessionId { get; private set; }

        public Task<CloseSessionResult> CloseSessionAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            ClosedSessionId = sessionId;
            return Task.FromResult(CloseResult);
        }
    }

    private sealed class CapturingProviderPublicationSessionService : IProviderPublicationSessionService
    {
        public OpenPublicationSessionResult OpenResult { get; init; } = OpenPublicationSessionResult.Success(Guid.NewGuid());

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
            BinaryData messageContent,
            string? mediaType,
            string? expiry,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
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
