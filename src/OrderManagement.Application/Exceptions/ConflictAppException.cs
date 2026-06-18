using OrderManagement.Application.Constants;

namespace OrderManagement.Application.Exceptions;

public sealed class ConflictAppException : AppException
{
    public ConflictAppException(
        string code,
        string message,
        IReadOnlyCollection<AppErrorDetail>? details = null,
        Exception? innerException = null)
        : base(code, message, StatusCodes.Conflict, details, innerException)
    {
    }

    public static ConflictAppException InsufficientStock(
        Guid productId,
        string productName,
        int requestedQuantity,
        int availableQuantity,
        string field)
    {
        return new ConflictAppException(
            ErrorCodes.InsufficientStock,
            $"Stock has changed. Product {productName} currently has only {availableQuantity} units available.",
            [
                AppErrorDetail.ForField(
                    field,
                    "Requested quantity exceeds available stock.",
                    new
                    {
                        productId,
                        requestedQuantity,
                        availableQuantity
                    })
            ]);
    }

    public static ConflictAppException RequestAlreadyInProgress()
    {
        return new ConflictAppException(
            ErrorCodes.RequestAlreadyInProgress,
            "A request with the same idempotency key is still being processed.");
    }

    public static ConflictAppException IdempotencyKeyReusedWithDifferentPayload()
    {
        return new ConflictAppException(
            ErrorCodes.IdempotencyKeyReusedWithDifferentPayload,
            "This idempotency key has already been used with a different request payload.");
    }
}