using Microsoft.AspNetCore.Mvc;
using RapidRedPanda.ISBM.Host.Contracts;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Services;
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

    public ProviderRequestServiceController(
        IProviderRequestSessionService requestSessionService,
        IUsernameTokenParser usernameTokenParser,
        ILogger<ProviderRequestServiceController> logger)
    {
        _requestSessionService = requestSessionService;
        _usernameTokenParser = usernameTokenParser;
        _logger = logger;
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
            request.FilterExpressions is { Count: > 0 })
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
            OpenProviderRequestSessionFailureKind.SecurityTokenFault => Conflict(new FaultResponse("SecurityTokenFault")),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
        };
    }

    [HttpGet("sessions/{sessionId}/request")]
    public async Task<IActionResult> ReadRequest(string sessionId, CancellationToken cancellationToken)
    {
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
            return NoContent();
        }

        if (result.Succeeded && result.Request is ReadRequestData request)
        {
            JsonElement messageContent;
            try
            {
                using JsonDocument messageContentDocument = JsonDocument.Parse(request.MessageContent);
                messageContent = messageContentDocument.RootElement.Clone();
            }
            catch (JsonException exception)
            {
                _logger.LogError(exception, "Stored request message content was not valid JSON.");
                return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
            }

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
            request.MessageContent.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ||
            !TryReadMessageContentMediaType(request.MessageContent, out string? mediaType))
        {
            return BadRequest(new FaultResponse("ParameterFault"));
        }

        PostResponseResult result;
        try
        {
            result = await _requestSessionService.PostResponseAsync(
                sessionId,
                requestMessageId,
                BinaryData.FromString(request.MessageContent.GetRawText()),
                mediaType ?? Request.ContentType,
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
            return NoContent();
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

    private static bool TryReadMessageContentMediaType(JsonElement messageContent, out string? mediaType)
    {
        mediaType = null;
        if (messageContent.ValueKind is not JsonValueKind.Object ||
            !messageContent.TryGetProperty("content", out _))
        {
            return false;
        }

        if (!messageContent.TryGetProperty("mediaType", out JsonElement mediaTypeElement) ||
            mediaTypeElement.ValueKind is JsonValueKind.Null)
        {
            return true;
        }

        if (mediaTypeElement.ValueKind is not JsonValueKind.String)
        {
            return false;
        }

        mediaType = mediaTypeElement.GetString();
        return !string.IsNullOrWhiteSpace(mediaType);
    }

    private ChannelCredentials? TryReadBasicCredentials()
    {
        string? authorizationHeader = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return null;
        }

        if (!AuthenticationHeaderValue.TryParse(authorizationHeader, out AuthenticationHeaderValue? authenticationHeader) ||
            !string.Equals(authenticationHeader.Scheme, "Basic", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(authenticationHeader.Parameter))
        {
            return null;
        }

        try
        {
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authenticationHeader.Parameter));
            int separatorIndex = decoded.IndexOf(':', StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                return null;
            }

            string username = decoded[..separatorIndex];
            string password = decoded[(separatorIndex + 1)..];

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            {
                return null;
            }

            return new ChannelCredentials(_usernameTokenParser.NormalizeUsername(username), password);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
