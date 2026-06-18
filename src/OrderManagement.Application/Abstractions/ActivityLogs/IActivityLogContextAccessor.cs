namespace OrderManagement.Application.Abstractions.ActivityLogs;

public interface IActivityLogContextAccessor
{
    string CorrelationId { get; }

    string? RequestPath { get; }

    string? HttpMethod { get; }

    Guid? UserId { get; }

    string? Username { get; }

    string? Role { get; }
}