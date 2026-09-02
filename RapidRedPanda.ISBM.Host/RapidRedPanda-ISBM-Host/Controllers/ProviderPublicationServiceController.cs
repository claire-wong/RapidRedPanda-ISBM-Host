using Microsoft.AspNetCore.Mvc;
using RapidRedPanda.ISBM.Host.Contracts;
using RapidRedPanda.ISBM.Host.Data;
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

    public ProviderPublicationServiceController(
        IProviderPublicationSessionService publicationSessionService,
        IUsernameTokenParser usernameTokenParser,
        ILogger<ProviderPublicationServiceController> logger)
    {
        _publicationSessionService = publicationSessionService;
        _usernameTokenParser = usernameTokenParser;
        _logger = logger;
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
            OpenPublicationSessionFailureKind.SecurityTokenFault => Conflict(new FaultResponse("SecurityTokenFault")),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
        };
    }

    [HttpPost("sessions/{sessionId}/publications")]
    public async Task<IActionResult> PostPublication(string sessionId, CancellationToken cancellationToken)
    {
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

        MessageContentValidationResult messageContentValidation = MessageContentSemanticsValidator.Validate(request.MessageContent);
        if (!messageContentValidation.Succeeded)
        {
            return BadRequest(new FaultResponse("ParameterFault"));
        }

        PostPublicationResult result;
        try
        {
            result = await _publicationSessionService.PostPublicationAsync(
                sessionId,
                request.Topics,
                BinaryData.FromString(request.MessageContent.GetRawText()),
                messageContentValidation.PayloadMediaType,
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
