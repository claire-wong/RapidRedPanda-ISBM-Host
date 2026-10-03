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
public sealed class ProviderRequestServiceController : Controller
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IProviderRequestSessionService _requestSessionService;
    private readonly IUsernameTokenParser _usernameTokenParser;
    private readonly ILogger<ProviderRequestServiceController> _logger;
    private readonly IIsbmOperationAuthorizationService? _authorization;

    public ProviderRequestServiceController(
        IProviderRequestSessionService requestSessionService,
        IUsernameTokenParser usernameTokenParser,
        ILogger<ProviderRequestServiceController> logger,
        IIsbmOperationAuthorizationService? authorization = null)
    {
        _requestSessionService = requestSessionService;
        _usernameTokenParser = usernameTokenParser;
        _logger = logger;
        _authorization = authorization;
    }

    [HttpPost("channels/{channelId}/provider-request-sessions")]
    public async Task<IActionResult> OpenProviderRequestSession(string channelId, CancellationToken cancellationToken)
    {
        OpenProviderRequestSessionRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<OpenProviderRequestSessionRequest>(
                Request.Body,
                JsonSerializerOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            return BadRequest(new FaultResponse("ParameterFault"));
        }

        if (request?.Topics is null ||
            request.Topics.Count == 0 ||
            request.Topics.Any(string.IsNullOrEmpty) ||
            !FilterExpressionJson.TrySerializeValid(request.FilterExpressions, out string? filterExpressionsJson))
        {
            return BadRequest(new FaultResponse("ParameterFault"));
        }

        OpenProviderRequestSessionResult result;
        try
        {
            result = await _requestSessionService.OpenProviderRequestSessionAsync(
                channelId,
                request.Topics,
                request.ListenerUrl,
                filterExpressionsJson,
                TryReadBasicCredentials(),
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected failure while opening provider request session.");
            return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
        }

        if (result.Succeeded && result.SessionId is Guid sessionId)
        {
            return StatusCode(StatusCodes.Status201Created, new OpenProviderRequestSessionResponse(sessionId));
        }

        return result.FailureKind switch
        {
            OpenProviderRequestSessionFailureKind.ParameterFault => BadRequest(new FaultResponse("ParameterFault")),
            OpenProviderRequestSessionFailureKind.ChannelFault => NotFound(new FaultResponse("ChannelFault")),
            OpenProviderRequestSessionFailureKind.InvalidChannelType => UnprocessableEntity(new FaultResponse("OperationFault")),
            OpenProviderRequestSessionFailureKind.SecurityTokenFault => NotFound(new FaultResponse("ChannelFault")),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
        };
    }

    [HttpGet("sessions/{sessionId}/request")]
    public async Task<IActionResult> ReadRequest(string sessionId, CancellationToken cancellationToken)
    {
        IActionResult? authorizationFailure = await AuthorizeSessionAsync(sessionId, cancellationToken);
        if (authorizationFailure is not null) return authorizationFailure;

        ReadRequestResult result;
        try
        {
            result = await _requestSessionService.ReadRequestAsync(sessionId, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected failure while reading provider request.");
            return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
        }

        if (result.Succeeded && !result.HasRequest)
        {
            return NotFound(new FaultResponse("MessageFault"));
        }

        if (result.Succeeded && result.Request is ReadRequestData request)
        {
            JsonElement messageContent = RestMessageContentMapper.ToRestJsonElement(request.MessageContent);

            return Ok(new ReadRequestResponse(request.MessageId, messageContent, request.Topics));
        }

        return result.FailureKind switch
        {
            ReadRequestFailureKind.ParameterFault => BadRequest(new FaultResponse("ParameterFault")),
            ReadRequestFailureKind.SessionFault => NotFound(new FaultResponse("SessionFault")),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
        };
    }

    [HttpDelete("sessions/{sessionId}/request")]
    public async Task<IActionResult> RemoveRequest(string sessionId, CancellationToken cancellationToken)
    {
        IActionResult? authorizationFailure = await AuthorizeSessionAsync(sessionId, cancellationToken);
        if (authorizationFailure is not null) return authorizationFailure;

        RemoveRequestResult result;
        try
        {
            result = await _requestSessionService.RemoveRequestAsync(sessionId, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected failure while removing provider request.");
            return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
        }

        if (result.Succeeded)
        {
            return NoContent();
        }

        return result.FailureKind switch
        {
            RemoveRequestFailureKind.ParameterFault => BadRequest(new FaultResponse("ParameterFault")),
            RemoveRequestFailureKind.SessionFault => NotFound(new FaultResponse("SessionFault")),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
        };
    }

    [HttpPost("sessions/{sessionId}/requests/{requestMessageId}/responses")]
    public async Task<IActionResult> PostResponse(string sessionId, string requestMessageId, CancellationToken cancellationToken)
    {
        IActionResult? authorizationFailure = await AuthorizeSessionAsync(sessionId, cancellationToken);
        if (authorizationFailure is not null) return authorizationFailure;

        if (!RestEnvelopeMediaTypeValidator.IsJsonEnvelopeMediaType(Request.ContentType))
        {
            return new UnsupportedMediaTypeResult();
        }

        PostResponseRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<PostResponseRequest>(
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

        PostResponseResult result;
        try
        {
            result = await _requestSessionService.PostResponseAsync(
                sessionId,
                requestMessageId,
                messageContent!,
                TryReadBasicCredentials(),
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected failure while posting provider request response.");
            return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
        }

        if (result.Succeeded && !result.ResponseCreated)
        {
            return NotFound(new FaultResponse("MessageFault"));
        }

        if (result.Succeeded && result.MessageId is Guid messageId)
        {
            return StatusCode(StatusCodes.Status201Created, new PostResponseResponse(messageId));
        }

        return result.FailureKind switch
        {
            PostResponseFailureKind.ParameterFault => BadRequest(new FaultResponse("ParameterFault")),
            PostResponseFailureKind.SessionFault => NotFound(new FaultResponse("SessionFault")),
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
            sessionId, IsbmSessionKind.ProviderRequest, TryReadBasicCredentials(), cancellationToken);
        return outcome switch
        {
            IsbmAuthorizationOutcome.Authorized => null,
            IsbmAuthorizationOutcome.NotFoundOrUnauthorized => NotFound(new FaultResponse("SessionFault")),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
        };
    }
}
