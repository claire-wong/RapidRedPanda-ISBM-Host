using Microsoft.AspNetCore.Mvc;
using RapidRedPanda.ISBM.Host.Contracts;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Messaging;
using RapidRedPanda.ISBM.Host.Services;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RapidRedPanda.ISBM.Host.Controllers;

[Route("isbm/2.0")]
public sealed class ConsumerPublicationServiceController : Controller
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IConsumerSubscriptionSessionService _subscriptionSessionService;
    private readonly IUsernameTokenParser _usernameTokenParser;
    private readonly ILogger<ConsumerPublicationServiceController> _logger;

    public ConsumerPublicationServiceController(
        IConsumerSubscriptionSessionService subscriptionSessionService,
        IUsernameTokenParser usernameTokenParser,
        ILogger<ConsumerPublicationServiceController> logger)
    {
        _subscriptionSessionService = subscriptionSessionService;
        _usernameTokenParser = usernameTokenParser;
        _logger = logger;
    }

    [HttpPost("channels/{channelId}/subscription-sessions")]
    public async Task<IActionResult> OpenSubscriptionSession(string channelId, CancellationToken cancellationToken)
    {
        OpenSubscriptionSessionRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<OpenSubscriptionSessionRequest>(
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

        OpenSubscriptionSessionResult result;
        try
        {
            result = await _subscriptionSessionService.OpenSubscriptionSessionAsync(
                channelId,
                request.Topics,
                TryReadBasicCredentials(),
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected failure while opening consumer subscription session.");
            return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
        }

        if (result.Succeeded && result.SessionId is Guid sessionId)
        {
            return StatusCode(StatusCodes.Status201Created, new OpenSubscriptionSessionResponse(sessionId));
        }

        return result.FailureKind switch
        {
            OpenSubscriptionSessionFailureKind.ParameterFault => BadRequest(new FaultResponse("ParameterFault")),
            OpenSubscriptionSessionFailureKind.ChannelFault => NotFound(new FaultResponse("ChannelFault")),
            OpenSubscriptionSessionFailureKind.SecurityTokenFault => Conflict(new FaultResponse("SecurityTokenFault")),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
        };
    }

    [HttpGet("sessions/{sessionId}/publication")]
    public async Task<IActionResult> ReadPublication(string sessionId, CancellationToken cancellationToken)
    {
        ReadPublicationResult result;
        try
        {
            result = await _subscriptionSessionService.ReadPublicationAsync(sessionId, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected failure while reading consumer publication.");
            return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
        }

        if (result.Succeeded && !result.HasPublication)
        {
            return NoContent();
        }

        if (result.Succeeded && result.Publication is ReadPublicationData publication)
        {
            JsonElement messageContent = RestMessageContentMapper.ToRestJsonElement(publication.MessageContent);

            return Ok(new ReadPublicationResponse(
                publication.MessageId,
                messageContent,
                publication.Topics));
        }

        return result.FailureKind switch
        {
            ReadPublicationFailureKind.ParameterFault => BadRequest(new FaultResponse("ParameterFault")),
            ReadPublicationFailureKind.SessionFault => NotFound(new FaultResponse("SessionFault")),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
        };
    }

    [HttpDelete("sessions/{sessionId}/publication")]
    public async Task<IActionResult> RemovePublication(string sessionId, CancellationToken cancellationToken)
    {
        RemovePublicationResult result;
        try
        {
            result = await _subscriptionSessionService.RemovePublicationAsync(sessionId, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected failure while removing consumer publication.");
            return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
        }

        if (result.Succeeded)
        {
            return NoContent();
        }

        return result.FailureKind switch
        {
            RemovePublicationFailureKind.ParameterFault => BadRequest(new FaultResponse("ParameterFault")),
            RemovePublicationFailureKind.SessionFault => NotFound(new FaultResponse("SessionFault")),
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
