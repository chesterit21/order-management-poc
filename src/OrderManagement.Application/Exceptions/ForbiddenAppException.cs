using OrderManagement.Application.Constants;

namespace OrderManagement.Application.Exceptions;

public sealed class ForbiddenAppException : AppException
{
    public ForbiddenAppException(
        string message = "Forbidden.",
        IReadOnlyCollection<AppErrorDetail>? details = null)
        : base(ErrorCodes.Forbidden, message, StatusCodes.Forbidden, details)
    {
    }
}