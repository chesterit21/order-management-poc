using OrderManagement.Application.Constants;

namespace OrderManagement.Application.Exceptions;

public sealed class BusinessRuleAppException : AppException
{
    public BusinessRuleAppException(
        string code,
        string message,
        IReadOnlyCollection<AppErrorDetail>? details = null)
        : base(code, message, StatusCodes.UnprocessableEntity, details)
    {
    }

    public static BusinessRuleAppException InvalidOrderTransition(
        string currentStatus,
        string targetStatus)
    {
        return new BusinessRuleAppException(
            ErrorCodes.InvalidOrderStatusTransition,
            $"Order status cannot be changed from {currentStatus} to {targetStatus}.",
            [
                AppErrorDetail.General(
                    "Invalid order status transition.",
                    new
                    {
                        currentStatus,
                        targetStatus
                    })
            ]);
    }

    public static BusinessRuleAppException PaymentNotAllowed(string currentStatus)
    {
        return new BusinessRuleAppException(
            ErrorCodes.PaymentNotAllowed,
            "Payment is only allowed when order status is Pending.",
            [
                AppErrorDetail.General(
                    "Payment cannot be processed for current order status.",
                    new
                    {
                        currentStatus
                    })
            ]);
    }
}