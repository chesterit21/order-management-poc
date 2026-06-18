namespace OrderManagement.Application.Abstractions.Idempotency;

public interface IRequestHashService
{
    string ComputeHash<TRequest>(TRequest request);

    string ComputeHashFromJson(string json);
}