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
public sealed class ConsumerRequestServiceController : Controller
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IConsumerRequestSessionService _requestSessionService;
    private readonly IUsernameTokenParser _usernameTokenParser;
    private readonly ILogger<ConsumerRequestServiceController> _logger;

    public ConsumerRequestServiceController(
        IConsumerRequestSessionService requestSessionService,
        IUsernameTokenParser usernameTokenParser,
        ILogger<ConsumerRequestServiceController> logger)
    {
        _requestSessionService = requestSessionService;
        _usernameTokenParser = usernameTokenParser;
        _logger = logger;
    }

    [HttpPost("channels/{channelId}/consumer-request-sessions")]
    public async Task<IActionResult> OpenConsumerRequestSession(string channelId, CancellationToken cancellationToken)
    {
        OpenConsumerRequestSessionResult result;
        try
        {
            result = await _requestSessionService.OpenConsumerRequestSessionAsync(
                channelId,
                TryReadBasicCredentials(),
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected failure while opening consumer request session.");
            return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
        }

        if (result.Succeeded && result.SessionId is Guid sessionId)
        {
            return StatusCode(StatusCodes.Status201Created, new OpenConsumerRequestSessionResponse(sessionId));
        }

        return result.FailureKind switch
        {
            OpenConsumerRequestSessionFailureKind.ParameterFault => BadRequest(new FaultResponse("ParameterFault")),
            OpenConsumerRequestSessionFailureKind.ChannelFault => NotFound(new FaultResponse("ChannelFault")),
            OpenConsumerRequestSessionFailureKind.SecurityTokenFault => Conflict(new FaultResponse("SecurityTokenFault")),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
        };
    }

    [HttpPost("sessions/{sessionId}/requests")]
    public async Task<IActionResult> PostRequest(string sessionId, CancellationToken cancellationToken)
    {
        if (!RestEnvelopeMediaTypeValidator.IsJsonEnvelopeMediaType(Request.ContentType))
        {
            return new UnsupportedMediaTypeResult();
        }

        PostRequestRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<PostRequestRequest>(
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

        PostRequestResult result;
        try
        {
            result = await _requestSessionService.PostRequestAsync(
                sessionId,
                request.Topics,
                messageContent!,
                request.Expiry,
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected failure while posting consumer request.");
            return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
        }

        if (result.Succeeded && result.MessageId is Guid messageId)
        {
            return StatusCode(StatusCodes.Status201Created, new PostRequestResponse(messageId));
        }

        return result.FailureKind switch
        {
            PostRequestFailureKind.ParameterFault => BadRequest(new FaultResponse("ParameterFault")),
            PostRequestFailureKind.SessionFault => NotFound(new FaultResponse("SessionFault")),
            PostRequestFailureKind.ChannelFault => NotFound(new FaultResponse("ChannelFault")),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
        };
    }

    [HttpDelete("sessions/{sessionId}/requests/{messageId}")]
    public async Task<IActionResult> ExpireRequest(string sessionId, string messageId, CancellationToken cancellationToken)
    {
        ExpireRequestResult result;
        try
        {
            result = await _requestSessionService.ExpireRequestAsync(
                sessionId,
                messageId,
                TryReadBasicCredentials(),
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected failure while expiring consumer request.");
            return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
        }

        if (result.Succeeded)
        {
            return NoContent();
        }

        return result.FailureKind switch
        {
            ExpireRequestFailureKind.ParameterFault => BadRequest(new FaultResponse("ParameterFault")),
            ExpireRequestFailureKind.SessionFault => NotFound(new FaultResponse("SessionFault")),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
        };
    }

    [HttpGet("sessions/{sessionId}/requests/{requestMessageId}/response")]
    public async Task<IActionResult> ReadResponse(string sessionId, string requestMessageId, CancellationToken cancellationToken)
    {
        ReadResponseResult result;
        try
        {
            result = await _requestSessionService.ReadResponseAsync(
                sessionId,
                requestMessageId,
                TryReadBasicCredentials(),
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected failure while reading response.");
            return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
        }

        if (result.Succeeded && !result.HasResponse)
        {
            return NoContent();
        }

        if (result.Succeeded && result.Response is ReadResponseData response)
        {
            JsonElement messageContent = RestMessageContentMapper.ToRestJsonElement(response.MessageContent);

            return Ok(new ReadResponseResponse(response.RequestMessageId, response.MessageId, messageContent));
        }

        return result.FailureKind switch
        {
            ReadResponseFailureKind.ParameterFault => BadRequest(new FaultResponse("ParameterFault")),
            ReadResponseFailureKind.SessionFault => NotFound(new FaultResponse("SessionFault")),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
        };
    }

    [HttpDelete("sessions/{sessionId}/requests/{requestMessageId}/response")]
    public async Task<IActionResult> RemoveResponse(string sessionId, string requestMessageId, CancellationToken cancellationToken)
    {
        RemoveResponseResult result;
        try
        {
            result = await _requestSessionService.RemoveResponseAsync(
                sessionId,
                requestMessageId,
                TryReadBasicCredentials(),
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected failure while removing response.");
            return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
        }

        if (result.Succeeded)
        {
            return NoContent();
        }

        return result.FailureKind switch
        {
            RemoveResponseFailureKind.ParameterFault => BadRequest(new FaultResponse("ParameterFault")),
            RemoveResponseFailureKind.SessionFault => NotFound(new FaultResponse("SessionFault")),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
        };
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
