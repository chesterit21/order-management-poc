namespace OrderManagement.Application.Abstractions.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}