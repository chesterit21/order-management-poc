using OrderManagement.Application.Constants;

namespace OrderManagement.Application.Exceptions;

public sealed class UnauthorizedAppException : AppException
{
    public UnauthorizedAppException(
        string message = "Unauthorized.",
        string code = ErrorCodes.Unauthorized,
        IReadOnlyCollection<AppErrorDetail>? details = null)
        : base(code, message, StatusCodes.Unauthorized, details)
    {
    }

    public static UnauthorizedAppException InvalidCredentials()
    {
        return new UnauthorizedAppException(
            "Invalid username or password.",
            ErrorCodes.InvalidCredentials);
    }
}