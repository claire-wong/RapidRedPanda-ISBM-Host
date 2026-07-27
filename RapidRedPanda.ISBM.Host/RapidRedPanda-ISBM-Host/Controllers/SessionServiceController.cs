using Microsoft.AspNetCore.Mvc;
using RapidRedPanda.ISBM.Host.Contracts;
using RapidRedPanda.ISBM.Host.Services;

namespace RapidRedPanda.ISBM.Host.Controllers;

[Route("isbm/2.0")]
public sealed class SessionServiceController : Controller
{
    private readonly ISessionCloseService _sessionCloseService;
    private readonly ILogger<SessionServiceController> _logger;

    public SessionServiceController(
        ISessionCloseService sessionCloseService,
        ILogger<SessionServiceController> logger)
    {
        _sessionCloseService = sessionCloseService;
        _logger = logger;
    }

    [HttpDelete("sessions/{sessionId}")]
    public async Task<IActionResult> CloseSession(string sessionId, CancellationToken cancellationToken)
    {
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
