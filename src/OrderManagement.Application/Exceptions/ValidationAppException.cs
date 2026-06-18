using OrderManagement.Application.Constants;

namespace OrderManagement.Application.Exceptions;

public sealed class ValidationAppException : AppException
{
    public ValidationAppException(
        string message = "Validation failed.",
        IReadOnlyCollection<AppErrorDetail>? details = null)
        : base(ErrorCodes.ValidationError, message, StatusCodes.UnprocessableEntity, details)
    {
    }
}

internal static class StatusCodes
{
    public const int BadRequest = 400;
    public const int Unauthorized = 401;
    public const int Forbidden = 403;
    public const int NotFound = 404;
    public const int Conflict = 409;
    public const int UnprocessableEntity = 422;
    public const int InternalServerError = 500;
}