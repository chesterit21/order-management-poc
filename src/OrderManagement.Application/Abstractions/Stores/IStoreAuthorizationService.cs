namespace OrderManagement.Application.Abstractions.Stores;

public interface IStoreAuthorizationService
{
    Task EnsureCanViewStoreAsync(
        Guid storeId,
        CancellationToken cancellationToken = default);

    Task EnsureCanManageStoreAsync(
        Guid storeId,
        CancellationToken cancellationToken = default);

    Task EnsureCanOperateStoreAsync(
        Guid storeId,
        CancellationToken cancellationToken = default);
}