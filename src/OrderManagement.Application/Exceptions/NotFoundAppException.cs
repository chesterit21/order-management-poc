using OrderManagement.Application.Constants;

namespace OrderManagement.Application.Exceptions;

public sealed class NotFoundAppException : AppException
{
    public NotFoundAppException(
        string message,
        string code = ErrorCodes.NotFound,
        IReadOnlyCollection<AppErrorDetail>? details = null)
        : base(code, message, StatusCodes.NotFound, details)
    {
    }

    public static NotFoundAppException User(Guid userId)
    {
        return new NotFoundAppException(
            "User was not found.",
            ErrorCodes.UserNotFound,
            [AppErrorDetail.ForField("userId", "User id does not exist.", new { userId })]);
    }

    public static NotFoundAppException Product(Guid productId)
    {
        return new NotFoundAppException(
            "Product was not found.",
            ErrorCodes.ProductNotFound,
            [AppErrorDetail.ForField("productId", "Product id does not exist.", new { productId })]);
    }

    public static NotFoundAppException Order(Guid orderId)
    {
        return new NotFoundAppException(
            "Order was not found.",
            ErrorCodes.OrderNotFound,
            [AppErrorDetail.ForField("orderId", "Order id does not exist.", new { orderId })]);
    }

    public static NotFoundAppException Payment(Guid paymentId)
    {
        return new NotFoundAppException(
            "Payment was not found.",
            ErrorCodes.PaymentNotFound,
            [AppErrorDetail.ForField("paymentId", "Payment id does not exist.", new { paymentId })]);
    }
}