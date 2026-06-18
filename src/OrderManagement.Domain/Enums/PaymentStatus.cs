namespace OrderManagement.Domain.Enums;

public enum PaymentStatus
{
    Pending = 1,
    Paid = 2,
    Failed = 3,
    Cancelled = 4,
    RefundRequired = 5,
    Refunded = 6
}