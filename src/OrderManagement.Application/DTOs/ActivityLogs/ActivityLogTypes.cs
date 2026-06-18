namespace OrderManagement.Application.DTOs.ActivityLogs;

public static class ActivityLogTypes
{
    public const string UserLoginAttempt = "UserLoginAttempt";
    public const string UserLoggedIn = "UserLoggedIn";
    public const string UserLoggedOut = "UserLoggedOut";
    public const string UserRegistration = "UserRegistration";
    public const string LoginFailed = "LoginFailed"; // Added for AuthService
    
    public const string ProductCreated = "ProductCreated";
    public const string ProductUpdated = "ProductUpdated";
    public const string ProductImageUploaded = "ProductImageUploaded";
    public const string ProductStockAdjusted = "ProductStockAdjusted";
    public const string ProductActivated = "ProductActivated";
    public const string ProductDeactivated = "ProductDeactivated";
    
    public const string OrderCreated = "OrderCreated";
    public const string OrderStatusUpdated = "OrderStatusUpdated";
    public const string OrderCancelled = "OrderCancelled";
    
    public const string PaymentCreated = "PaymentCreated";
    public const string PaymentProcessed = "PaymentProcessed";
    public const string PaymentRefunded = "PaymentRefunded";
    
    public const string StoreCreated = "StoreCreated";
    public const string StoreUpdated = "StoreUpdated";
    public const string StoreOperatorCreated = "StoreOperatorCreated";
    public const string StoreOperatorDeactivated = "StoreOperatorDeactivated";
    
    // Adding missing activity log types for order operations
    public const string OrderStatusChangeStarted = "OrderStatusChangeStarted";
    public const string OrderStatusChangeCompleted = "OrderStatusChangeCompleted";
    public const string OrderStatusRejected = "OrderStatusRejected";
    public const string OrderCancelCompleted = "OrderCancelCompleted";
    public const string OrderCancelStarted = "OrderCancelStarted";
    public const string StockDeduction = "StockDeduction";
    public const string StockRestored = "StockRestored";
    public const string StockNotRestored = "StockNotRestored";
    public const string PaymentRefundRequired = "PaymentRefundRequired";
    
    // Adding missing activity log types
    public const string PaymentCreateStarted = "PaymentCreateStarted";
    public const string PaymentRejected = "PaymentRejected";
    public const string PaymentPaid = "PaymentPaid";
    public const string PaymentFailed = "PaymentFailed";
    
    public const string IdempotencyAccepted = "IdempotencyAccepted";
    public const string IdempotencyConflict = "IdempotencyConflict";
    public const string IdempotencyReplayReturned = "IdempotencyReplayReturned";
    
    public const string RequestCompleted = "RequestCompleted";
    public const string RequestFailed = "RequestFailed";
    
    public const string OrderStatusChanged = "OrderStatusChanged";
}