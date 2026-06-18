namespace OrderManagement.Domain.Enums;

public enum IdempotencyStatus
{
    InProgress = 1,
    Completed = 2,
    Failed = 3
}