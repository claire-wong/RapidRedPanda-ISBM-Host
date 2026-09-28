using Microsoft.AspNetCore.Mvc;
using RapidRedPanda.ISBM.Host.Contracts;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Messaging;
using RapidRedPanda.ISBM.Host.Services;
using RapidRedPanda.ISBM.Host.Validation;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RapidRedPanda.ISBM.Host.Controllers;

[Route("isbm/2.0")]
public sealed class ProviderPublicationServiceController : Controller
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IProviderPublicationSessionService _publicationSessionService;
    private readonly IUsernameTokenParser _usernameTokenParser;
    private readonly ILogger<ProviderPublicationServiceController> _logger;
    private readonly IIsbmOperationAuthorizationService? _authorization;

    public ProviderPublicationServiceController(
        IProviderPublicationSessionService publicationSessionService,
        IUsernameTokenParser usernameTokenParser,
        ILogger<ProviderPublicationServiceController> logger,
        IIsbmOperationAuthorizationService? authorization = null)
    {
        _publicationSessionService = publicationSessionService;
        _usernameTokenParser = usernameTokenParser;
        _logger = logger;
        _authorization = authorization;
    }

    [HttpPost("channels/{channelId}/publication-sessions")]
    public async Task<IActionResult> OpenPublicationSession(string channelId, CancellationToken cancellationToken)
    {
        OpenPublicationSessionResult result;
        try
        {
            result = await _publicationSessionService.OpenPublicationSessionAsync(
                channelId,
                TryReadBasicCredentials(),
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected failure while opening provider publication session.");
            return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
        }

        if (result.Succeeded && result.SessionId is Guid sessionId)
        {
            return StatusCode(StatusCodes.Status201Created, new OpenPublicationSessionResponse(sessionId));
        }

        return result.FailureKind switch
        {
            OpenPublicationSessionFailureKind.ParameterFault => BadRequest(new FaultResponse("ParameterFault")),
            OpenPublicationSessionFailureKind.ChannelFault => NotFound(new FaultResponse("ChannelFault")),
            OpenPublicationSessionFailureKind.SecurityTokenFault => NotFound(new FaultResponse("ChannelFault")),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
        };
    }

    [HttpPost("sessions/{sessionId}/publications")]
    public async Task<IActionResult> PostPublication(string sessionId, CancellationToken cancellationToken)
    {
        IActionResult? authorizationFailure = await AuthorizeSessionAsync(sessionId, cancellationToken);
        if (authorizationFailure is not null) return authorizationFailure;

        if (!RestEnvelopeMediaTypeValidator.IsJsonEnvelopeMediaType(Request.ContentType))
        {
            return new UnsupportedMediaTypeResult();
        }

        PostPublicationRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<PostPublicationRequest>(
                Request.Body,
                JsonSerializerOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            return BadRequest(new FaultResponse("ParameterFault"));
        }

        if (request is null ||
            request.MessageContent.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return BadRequest(new FaultResponse("ParameterFault"));
        }

        if (!RestMessageContentMapper.TryFromRest(request.MessageContent, out InternalMessageContent? messageContent))
        {
            return BadRequest(new FaultResponse("ParameterFault"));
        }

        PostPublicationResult result;
        try
        {
            result = await _publicationSessionService.PostPublicationAsync(
                sessionId,
                request.Topics,
                messageContent!,
                request.Expiry,
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected failure while posting provider publication.");
            return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
        }

        if (result.Succeeded && result.PublicationId is Guid publicationId)
        {
            return StatusCode(StatusCodes.Status201Created, new PostPublicationResponse(publicationId));
        }

        return result.FailureKind switch
        {
            PostPublicationFailureKind.ParameterFault => BadRequest(new FaultResponse("ParameterFault")),
            PostPublicationFailureKind.SessionFault => NotFound(new FaultResponse("SessionFault")),
            PostPublicationFailureKind.ChannelFault => NotFound(new FaultResponse("ChannelFault")),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
        };
    }

    [HttpDelete("sessions/{sessionId}/publications/{messageId}")]
    public async Task<IActionResult> ExpirePublication(
        string sessionId,
        string messageId,
        CancellationToken cancellationToken)
    {
        IActionResult? authorizationFailure = await AuthorizeSessionAsync(sessionId, cancellationToken);
        if (authorizationFailure is not null) return authorizationFailure;

        ExpirePublicationResult result;
        try
        {
            result = await _publicationSessionService.ExpirePublicationAsync(
                sessionId,
                messageId,
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected failure while expiring provider publication.");
            return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
        }

        if (result.Succeeded)
        {
            return NoContent();
        }

        return result.FailureKind switch
        {
            ExpirePublicationFailureKind.ParameterFault => BadRequest(new FaultResponse("ParameterFault")),
            ExpirePublicationFailureKind.SessionFault => NotFound(new FaultResponse("SessionFault")),
            ExpirePublicationFailureKind.MessageFault => NotFound(new FaultResponse("MessageFault")),
            ExpirePublicationFailureKind.ChannelFault => NotFound(new FaultResponse("ChannelFault")),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
        };
    }

    private ChannelCredentials? TryReadBasicCredentials()
    {
        return BasicAuthenticationCredentials.Read(Request, _usernameTokenParser);
    }

    private async Task<IActionResult?> AuthorizeSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (_authorization is null) return null;
        IsbmAuthorizationOutcome outcome = await _authorization.AuthorizeSessionAsync(
            sessionId, IsbmSessionKind.ProviderPublication, TryReadBasicCredentials(), cancellationToken);
        return outcome switch
        {
            IsbmAuthorizationOutcome.Authorized => null,
            IsbmAuthorizationOutcome.NotFoundOrUnauthorized => NotFound(new FaultResponse("SessionFault")),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
        };
    }
}
