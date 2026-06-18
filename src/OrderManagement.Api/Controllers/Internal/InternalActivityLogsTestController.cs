using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Extensions;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.ActivityLogs;

namespace OrderManagement.Api.Controllers.Internal;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.ApplicationAdminOrDevOps)]
[Route("api/v1/internal/activity-logs/test")]
public sealed class InternalActivityLogsTestController : ControllerBase
{
    private readonly IActivityLogWriter _activityLogWriter;

    public InternalActivityLogsTestController(IActivityLogWriter activityLogWriter)
    {
        _activityLogWriter = activityLogWriter;
    }

    [HttpPost]
    public IActionResult EnqueueTestLog()
    {
        var written = _activityLogWriter.TryWrite(
            ActivityLogTypes.RequestCompleted,
            statusCode: StatusCodes.Status200OK,
            metadata: new
            {
                source = "manual-test",
                message = "Activity log test message"
            });

        return Ok(new
        {
            enqueued = written
        });
    }
}