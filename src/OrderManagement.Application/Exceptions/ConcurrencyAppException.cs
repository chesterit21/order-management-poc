using OrderManagement.Application.Constants;

namespace OrderManagement.Application.Exceptions;

public sealed class ConcurrencyAppException : AppException
{
    public ConcurrencyAppException(
        string message,
        IReadOnlyCollection<AppErrorDetail>? details = null)
        : base(ErrorCodes.ConcurrentUpdateConflict, message, StatusCodes.Conflict, details)
    {
    }

    public static ConcurrencyAppException RowVersionMismatch(
        long expected,
        long current)
    {
        return new ConcurrencyAppException(
            "Order has been modified by another user. Please refresh and try again.",
            [
                AppErrorDetail.ForField(
                    "expectedRowVersion",
                    "Expected row version does not match current row version.",
                    new
                    {
                        expected,
                        current
                    })
            ]);
    }
}