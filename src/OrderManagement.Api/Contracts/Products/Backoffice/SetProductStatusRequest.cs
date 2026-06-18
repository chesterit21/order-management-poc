namespace OrderManagement.Api.Contracts.Products.Backoffice;

public sealed record SetProductStatusRequest
{
    public bool IsActive { get; init; }

    public long ExpectedRowVersion { get; init; }
}