namespace OrderManagement.Domain.Enums;

public enum InventoryMovementType
{
    OrderCreatedDeduction = 1,
    OrderCancelledRestore = 2,
    OrderCancelledNoRestore = 3,
    ManualAdjustment = 4,
    StockDeduction = 5
}