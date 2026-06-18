namespace OrderManagement.Application.DTOs.Idempotency;

public sealed class IdempotencyProcessResult
{
    private IdempotencyProcessResult(
        IdempotencyProcessDecision decision,
        Guid? recordId,
        int? storedStatusCode,
        string? storedResponseBody)
    {
        Decision = decision;
        RecordId = recordId;
        StoredStatusCode = storedStatusCode;
        StoredResponseBody = storedResponseBody;
    }

    public IdempotencyProcessDecision Decision { get; }

    public Guid? RecordId { get; }

    public int? StoredStatusCode { get; }

    public string? StoredResponseBody { get; }

    public bool ShouldProcess => Decision == IdempotencyProcessDecision.ProcessRequest;

    public bool HasStoredResponse => Decision == IdempotencyProcessDecision.ReturnStoredResponse;

    public static IdempotencyProcessResult ProcessRequest(Guid recordId)
    {
        return new IdempotencyProcessResult(
            IdempotencyProcessDecision.ProcessRequest,
            recordId,
            null,
            null);
    }

    public static IdempotencyProcessResult ReturnStoredResponse(
        int statusCode,
        string responseBody)
    {
        return new IdempotencyProcessResult(
            IdempotencyProcessDecision.ReturnStoredResponse,
            null,
            statusCode,
            responseBody);
    }
}

public enum IdempotencyProcessDecision
{
    ProcessRequest = 1,
    ReturnStoredResponse = 2
}