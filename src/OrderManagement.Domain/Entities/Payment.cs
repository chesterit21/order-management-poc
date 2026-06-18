using OrderManagement.Domain.Common;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.ValueObjects;

namespace OrderManagement.Domain.Entities;

public sealed class Payment : AuditableEntity
{
    private Payment()
    {
    }

    private Payment(
        Guid id,
        Guid orderId,
        decimal amount,
        PaymentStatus status,
        string provider,
        string? paymentReference,
        DateTimeOffset createdAt)
        : base(id)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("Order id is required.", nameof(orderId));
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("Payment provider is required.", nameof(provider));
        }

        OrderId = orderId;
        Amount = Money.From(amount);
        Status = status;
        Provider = provider.Trim();
        PaymentReference = paymentReference;
        SetCreatedAt(createdAt);
    }

    public Guid OrderId { get; private set; }

    public Money Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public string? PaymentReference { get; private set; }

    public static Payment CreatePaid(
        Guid orderId,
        decimal amount,
        string provider,
        string paymentReference,
        DateTimeOffset now)
    {
        return new Payment(
            Guid.NewGuid(),
            orderId,
            amount,
            PaymentStatus.Paid,
            provider,
            paymentReference,
            now);
    }

    public static Payment CreateFailed(
        Guid orderId,
        decimal amount,
        string provider,
        string? paymentReference,
        DateTimeOffset now)
    {
        return new Payment(
            Guid.NewGuid(),
            orderId,
            amount,
            PaymentStatus.Failed,
            provider,
            paymentReference,
            now);
    }

    public static Payment Rehydrate(
        Guid id,
        Guid orderId,
        decimal amount,
        PaymentStatus status,
        string provider,
        string? paymentReference,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        var payment = new Payment(
            id,
            orderId,
            amount,
            status,
            provider,
            paymentReference,
            createdAt);

        payment.SetUpdatedAt(updatedAt);

        return payment;
    }

    public void MarkRefundRequired(DateTimeOffset now)
    {
        if (Status != PaymentStatus.Paid)
        {
            throw new InvalidOperationException("Only paid payment can be marked as refund required.");
        }

        Status = PaymentStatus.RefundRequired;
        SetUpdatedAt(now);
    }

    public void MarkRefunded(DateTimeOffset now)
    {
        if (Status is not PaymentStatus.Paid and not PaymentStatus.RefundRequired)
        {
            throw new InvalidOperationException("Only paid or refund required payment can be marked as refunded.");
        }

        Status = PaymentStatus.Refunded;
        SetUpdatedAt(now);
    }
}