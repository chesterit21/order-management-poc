namespace OrderManagement.Application.Exceptions;

public sealed class AppErrorDetail
{
    public AppErrorDetail(string? field, string message, object? metadata = null)
    {
        Field = field;
        Message = message;
        Metadata = metadata;
    }

    public string? Field { get; }

    public string Message { get; }

    public object? Metadata { get; }

    public static AppErrorDetail ForField(string field, string message, object? metadata = null)
    {
        return new AppErrorDetail(field, message, metadata);
    }

    public static AppErrorDetail General(string message, object? metadata = null)
    {
        return new AppErrorDetail(null, message, metadata);
    }
}