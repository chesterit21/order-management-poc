namespace OrderManagement.Application.DTOs.Orders;

public sealed class CreateOrderOperationResult
{
    private CreateOrderOperationResult(
        bool isStoredResponse,
        int statusCode,
        string? storedResponseBody,
        CreateOrderResult? response)
    {
        IsStoredResponse = isStoredResponse;
        StatusCode = statusCode;
        StoredResponseBody = storedResponseBody;
        Response = response;
    }

    public bool IsStoredResponse { get; }

    public int StatusCode { get; }

    public string? StoredResponseBody { get; }

    public CreateOrderResult? Response { get; }

    public static CreateOrderOperationResult Created(CreateOrderResult response)
    {
        return new CreateOrderOperationResult(
            false,
            201,
            null,
            response);
    }

    public static CreateOrderOperationResult Stored(int statusCode, string responseBody)
    {
        return new CreateOrderOperationResult(
            true,
            statusCode,
            responseBody,
            null);
    }
}