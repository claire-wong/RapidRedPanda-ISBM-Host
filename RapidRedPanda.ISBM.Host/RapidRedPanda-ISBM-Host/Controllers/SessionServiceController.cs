using Microsoft.AspNetCore.Mvc;
using RapidRedPanda.ISBM.Host.Contracts;
using RapidRedPanda.ISBM.Host.Services;
using RapidRedPanda.ISBM.Host.Data;

namespace RapidRedPanda.ISBM.Host.Controllers;

[Route("isbm/2.0")]
public sealed class SessionServiceController : Controller
{
    private readonly ISessionCloseService _sessionCloseService;
    private readonly ILogger<SessionServiceController> _logger;
    private readonly IUsernameTokenParser? _usernameTokenParser;
    private readonly IIsbmOperationAuthorizationService? _authorization;

    public SessionServiceController(
        ISessionCloseService sessionCloseService,
        ILogger<SessionServiceController> logger,
        IUsernameTokenParser? usernameTokenParser = null,
        IIsbmOperationAuthorizationService? authorization = null)
    {
        _sessionCloseService = sessionCloseService;
        _logger = logger;
        _usernameTokenParser = usernameTokenParser;
        _authorization = authorization;
    }

    [HttpDelete("sessions/{sessionId}")]
    public async Task<IActionResult> CloseSession(string sessionId, CancellationToken cancellationToken)
    {
        if (_authorization is not null && _usernameTokenParser is not null)
        {
            ChannelCredentials? credentials = BasicAuthenticationCredentials.Read(Request, _usernameTokenParser);
            IsbmAuthorizationOutcome authorization = await _authorization.AuthorizeAnySessionAsync(sessionId, credentials, cancellationToken);
            if (authorization == IsbmAuthorizationOutcome.NotFoundOrUnauthorized)
            {
                return NotFound(new FaultResponse("SessionFault"));
            }
            if (authorization == IsbmAuthorizationOutcome.Failure)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
            }
        }

        CloseSessionResult result;
        try
        {
            result = await _sessionCloseService.CloseSessionAsync(sessionId, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected failure while closing session.");
            return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
        }

        if (result.Succeeded)
        {
            return NoContent();
        }

        return result.FailureKind switch
        {
            CloseSessionFailureKind.ParameterFault => BadRequest(new FaultResponse("ParameterFault")),
            CloseSessionFailureKind.SessionFault => NotFound(new FaultResponse("SessionFault")),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
        };
    }
}
