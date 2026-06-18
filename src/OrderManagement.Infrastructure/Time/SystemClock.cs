using OrderManagement.Application.Abstractions.Time;

namespace OrderManagement.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}