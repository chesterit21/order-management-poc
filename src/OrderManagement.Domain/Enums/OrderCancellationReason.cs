namespace OrderManagement.Domain.Enums;

public enum OrderCancellationReason
{
    CustomerRequested = 1,
    StockUnavailable = 2,
    InventoryMismatch = 3,
    OperationalIssue = 4,
    FraudSuspected = 5
}