using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Extensions;
using OrderManagement.Application.Constants;
using OrderManagement.Application.Exceptions;

namespace OrderManagement.Api.Controllers;

/// <summary>
/// Diagnostic endpoints for verifying the health of the API pipeline
/// (OK response, business rule exception handling, unhandled exception handling).
///
/// Security: All endpoints require ApplicationAdminOrDevOps authentication.
/// The error-throwing endpoints are additionally restricted to non-Production
/// environments to prevent abuse in production.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.ApplicationAdminOrDevOps)]
[Route("api/v1/diagnostics")]
public sealed class DiagnosticsController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;

    public DiagnosticsController(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    [HttpGet("ok")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult OkResponse()
    {
        return Ok(new
        {
            message = "OK",
            correlationId = HttpContext.Response.Headers["X-Correlation-ID"].ToString()
        });
    }

    [HttpGet("app-error")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult AppError()
    {
        EnsureNonProduction();

        throw new BusinessRuleAppException(
            ErrorCodes.InvalidOrderStatusTransition,
            "Diagnostic business rule exception.");
    }

    [HttpGet("unhandled-error")]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult UnhandledError()
    {
        EnsureNonProduction();

        throw new InvalidOperationException("Diagnostic unhandled exception.");
    }

    private void EnsureNonProduction()
    {
        if (_environment.IsProduction())
        {
            throw new ForbiddenAppException(
                "Diagnostic error endpoints are disabled in production environment.");
        }
    }
}