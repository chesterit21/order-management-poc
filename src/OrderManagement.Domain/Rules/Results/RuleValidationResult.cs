namespace OrderManagement.Domain.Rules.Results;

public sealed class RuleValidationResult
{
    private RuleValidationResult(bool isAllowed, string? errorCode, string? errorMessage)
    {
        IsAllowed = isAllowed;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool IsAllowed { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public static RuleValidationResult Allowed()
    {
        return new RuleValidationResult(true, null, null);
    }

    public static RuleValidationResult Rejected(string errorCode, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            throw new ArgumentException("Error code is required.", nameof(errorCode));
        }

        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("Error message is required.", nameof(errorMessage));
        }

        return new RuleValidationResult(false, errorCode, errorMessage);
    }
}