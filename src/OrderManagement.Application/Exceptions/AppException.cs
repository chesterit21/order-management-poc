namespace OrderManagement.Application.Exceptions;

public abstract class AppException : Exception
{
    protected AppException(
        string code,
        string message,
        int statusCode,
        IReadOnlyCollection<AppErrorDetail>? details = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        StatusCode = statusCode;
        Details = details ?? [];
    }

    public string Code { get; }

    public int StatusCode { get; }

    public IReadOnlyCollection<AppErrorDetail> Details { get; }
}