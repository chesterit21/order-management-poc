using OrderManagement.Domain.Constants;

namespace OrderManagement.Domain.ValueObjects;

public readonly record struct Sku
{
    public Sku(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("SKU is required.", nameof(value));
        }

        var normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length > DomainConstants.MaxSkuLength)
        {
            throw new ArgumentException($"SKU cannot be longer than {DomainConstants.MaxSkuLength} characters.", nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public static Sku From(string value)
    {
        return new Sku(value);
    }

    public override string ToString()
    {
        return Value;
    }
}