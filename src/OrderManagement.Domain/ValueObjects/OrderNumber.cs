using OrderManagement.Domain.Constants;

namespace OrderManagement.Domain.ValueObjects;

public readonly record struct OrderNumber
{
    public OrderNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Order number is required.", nameof(value));
        }

        var normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length > DomainConstants.MaxOrderNumberLength)
        {
            throw new ArgumentException($"Order number cannot be longer than {DomainConstants.MaxOrderNumberLength} characters.", nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public static OrderNumber From(string value)
    {
        return new OrderNumber(value);
    }

    public static OrderNumber Generate(DateTimeOffset now, long sequence)
    {
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Sequence must be greater than zero.");
        }

        var value = $"{DomainConstants.OrderNumberPrefix}-{now:yyyyMMdd}-{sequence:000000}";

        return new OrderNumber(value);
    }

    public override string ToString()
    {
        return Value;
    }
}