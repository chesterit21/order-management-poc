namespace OrderManagement.Application.Constants;

public static class ErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string Forbidden = "FORBIDDEN";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string BusinessRuleViolation = "BUSINESS_RULE_VIOLATION";
    public const string ConcurrencyError = "CONCURRENCY_ERROR";
    
    public const string UserNotFound = "USER_NOT_FOUND";
    public const string UserAlreadyExists = "USER_ALREADY_EXISTS";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string AccountInactive = "ACCOUNT_INACTIVE";
    
    public const string ProductNotFound = "PRODUCT_NOT_FOUND";
    public const string InsufficientStock = "INSUFFICIENT_STOCK";
    public const string ProductInactive = "PRODUCT_INACTIVE";
    
    public const string OrderNotFound = "ORDER_NOT_FOUND";
    public const string OrderAlreadyCancelled = "ORDER_ALREADY_CANCELLED";
    public const string OrderNotCancellable = "ORDER_NOT_CANCELLABLE";
    public const string OrderStatusTransitionInvalid = "ORDER_STATUS_TRANSITION_INVALID";
    
    public const string PaymentNotFound = "PAYMENT_NOT_FOUND";
    public const string PaymentAlreadyProcessed = "PAYMENT_ALREADY_PROCESSED";
    public const string PaymentAmountMismatch = "PAYMENT_AMOUNT_MISMATCH";
    
    public const string IdempotencyKeyConflict = "IDEMPOTENCY_KEY_CONFLICT";
    
    public const string StoreNotFound = "STORE_NOT_FOUND";
    public const string StoreAlreadyExists = "STORE_ALREADY_EXISTS";
    public const string StoreAccessDenied = "STORE_ACCESS_DENIED";
    public const string InvalidStoreRole = "INVALID_STORE_ROLE";
    public const string SellerOperatorCannotActAsBuyer = "SELLER_OPERATOR_CANNOT_ACT_AS_BUYER";
    
    public const string ProductStoreNotAssigned = "PRODUCT_STORE_NOT_ASSIGNED";
    public const string MixedStoreOrderNotAllowed = "MIXED_STORE_ORDER_NOT_ALLOWED";
    public const string InvalidStockAdjustment = "INVALID_STOCK_ADJUSTMENT";
    
    // Adding missing error codes
    public const string InvalidOrderStatusTransition = "INVALID_ORDER_STATUS_TRANSITION";
    public const string CancelledStatusRequiresCancelEndpoint = "CANCELLED_STATUS_REQUIRES_CANCEL_ENDPOINT";
    public const string ConcurrentUpdateConflict = "CONCURRENT_UPDATE_CONFLICT";
    
    // Adding missing error codes from build errors
    public const string RequestAlreadyInProgress = "REQUEST_ALREADY_IN_PROGRESS";
    public const string IdempotencyKeyReusedWithDifferentPayload = "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD";
    public const string PaymentNotAllowed = "PAYMENT_NOT_ALLOWED";
    public const string InvalidCancellationReason = "INVALID_CANCELLATION_REASON";
    public const string Order_CannotUpdateToCancelledStatus = "ORDER_CANNOT_UPDATE_TO_CANCELLED_STATUS";
    public const string DatabaseConstraintViolation = "DATABASE_CONSTRAINT_VIOLATION";
    public const string InternalServerError = "INTERNAL_SERVER_ERROR";
    public const string OrderTerminalState = "ORDER_TERMINAL_STATE";
    public const string PaymentAlreadyPaid = "PAYMENT_ALREADY_PAID";
    public const string IdempotencyKeyRequired = "IDEMPOTENCY_KEY_REQUIRED";
}