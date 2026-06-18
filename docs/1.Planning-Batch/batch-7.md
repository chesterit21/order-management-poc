Siap bro. Kita masuk **Batch 7: Idempotency Service**. Ini batch krusial sebelum `Create Order`, karena endpoint `POST /orders` nanti wajib aman dari:

```text
1. User double-click submit.
2. Client retry karena timeout/network.
3. Dua request dengan Idempotency-Key sama datang bersamaan sebelum commit.
4. Key sama tapi payload beda.
5. Request sebelumnya masih InProgress.
```

Di batch ini kita implement fondasi idempotency dulu. Nanti di **Batch 8 Create Order Transaction**, flow-nya akan pakai service ini di dalam transaksi order.

***

# Batch 7 — Idempotency Service

## Design Final Batch 7

Behavior yang kita implement:

```text
POST /api/v1/orders wajib punya Idempotency-Key.

Backend akan:
1. Normalize request payload.
2. Hash payload dengan SHA-256.
3. Insert idempotency record status InProgress.
4. Jika insert sukses, request ini menjadi owner proses.
5. Jika key sudah ada:
   - hash beda => 409 IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD
   - status Completed => return stored response
   - status InProgress dan belum expired => 409 REQUEST_ALREADY_IN_PROGRESS
   - status Failed => return stored failed response jika ada
6. Setelah operation sukses, caller mark Completed.
7. Jika operation gagal terkontrol, caller bisa mark Failed.
```

Important: idempotency record insert harus atomic via unique constraint:

```sql
UNIQUE (user_id, key, endpoint)
```

Itu sudah ada dari migration batch sebelumnya.

***

# 1. Application DTO/Model untuk Idempotency

Buat folder:

```bash
mkdir -p src/OrderManagement.Application/DTOs/Idempotency
```

***

## 1.1 `IdempotencyProcessResult.cs`

Create file:

```text
src/OrderManagement.Application/DTOs/Idempotency/IdempotencyProcessResult.cs
```

Isi:

```csharp
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
```

***

## 1.2 `CreateIdempotencyRecordRequest.cs`

Create file:

```text
src/OrderManagement.Application/DTOs/Idempotency/CreateIdempotencyRecordRequest.cs
```

Isi:

```csharp
namespace OrderManagement.Application.DTOs.Idempotency;

public sealed class CreateIdempotencyRecordRequest
{
    public required string Key { get; init; }

    public required Guid UserId { get; init; }

    public required string Endpoint { get; init; }

    public required string RequestHash { get; init; }

    public required DateTimeOffset LockedUntil { get; init; }

    public required DateTimeOffset Now { get; init; }
}
```

***

## 1.3 `StoredIdempotencyResponse.cs`

Create file:

```text
src/OrderManagement.Application/DTOs/Idempotency/StoredIdempotencyResponse.cs
```

Isi:

```csharp
namespace OrderManagement.Application.DTOs.Idempotency;

public sealed class StoredIdempotencyResponse
{
    public required int StatusCode { get; init; }

    public required string ResponseBody { get; init; }
}
```

***

# 2. Application Abstractions

## 2.1 `IRequestHashService.cs`

Replace:

```text
src/OrderManagement.Application/Abstractions/Idempotency/IRequestHashService.cs
```

Isi:

```csharp
namespace OrderManagement.Application.Abstractions.Idempotency;

public interface IRequestHashService
{
    string ComputeHash<TRequest>(TRequest request);

    string ComputeHashFromJson(string json);
}
```

***

## 2.2 `IIdempotencyService.cs`

Replace:

```text
src/OrderManagement.Application/Abstractions/Idempotency/IIdempotencyService.cs
```

Isi:

```csharp
using OrderManagement.Application.DTOs.Idempotency;

namespace OrderManagement.Application.Abstractions.Idempotency;

public interface IIdempotencyService
{
    Task<IdempotencyProcessResult> BeginAsync(
        string key,
        Guid userId,
        string endpoint,
        string requestHash,
        CancellationToken cancellationToken = default);

    Task MarkCompletedAsync(
        Guid recordId,
        int responseStatusCode,
        string responseBody,
        string resourceType,
        Guid resourceId,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(
        Guid recordId,
        int responseStatusCode,
        string responseBody,
        CancellationToken cancellationToken = default);
}
```

***

## 2.3 `IIdempotencyRepository.cs`

Replace:

```text
src/OrderManagement.Application/Abstractions/Repositories/IIdempotencyRepository.cs
```

Isi:

```csharp
using OrderManagement.Application.DTOs.Idempotency;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Abstractions.Repositories;

public interface IIdempotencyRepository
{
    Task<bool> TryInsertInProgressAsync(
        CreateIdempotencyRecordRequest request,
        CancellationToken cancellationToken = default);

    Task<IdempotencyRecord?> GetByKeyAsync(
        Guid userId,
        string key,
        string endpoint,
        CancellationToken cancellationToken = default);

    Task MarkCompletedAsync(
        Guid recordId,
        int responseStatusCode,
        string responseBody,
        string resourceType,
        Guid resourceId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(
        Guid recordId,
        int responseStatusCode,
        string responseBody,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
```

***

# 3. Request Hash Service

## `RequestHashService.cs`

Replace:

```text
src/OrderManagement.Infrastructure/Idempotency/RequestHashService.cs
```

Isi:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OrderManagement.Application.Abstractions.Idempotency;

namespace OrderManagement.Infrastructure.Idempotency;

public sealed class RequestHashService : IRequestHashService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public string ComputeHash<TRequest>(TRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var json = JsonSerializer.Serialize(request, SerializerOptions);

        return ComputeHashFromJson(json);
    }

    public string ComputeHashFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("JSON payload is required.", nameof(json));
        }

        var normalizedJson = NormalizeJson(json);
        var bytes = Encoding.UTF8.GetBytes(normalizedJson);
        var hash = SHA256.HashData(bytes);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeJson(string json)
    {
        var node = JsonNode.Parse(json)
            ?? throw new ArgumentException("JSON payload is invalid.", nameof(json));

        var normalizedNode = NormalizeNode(node);

        return normalizedNode.ToJsonString(SerializerOptions);
    }

    private static JsonNode NormalizeNode(JsonNode node)
    {
        return node switch
        {
            JsonObject jsonObject => NormalizeObject(jsonObject),
            JsonArray jsonArray => NormalizeArray(jsonArray),
            JsonValue jsonValue => jsonValue.DeepClone(),
            _ => node.DeepClone()
        };
    }

    private static JsonObject NormalizeObject(JsonObject jsonObject)
    {
        var normalized = new JsonObject();

        foreach (var property in jsonObject.OrderBy(property => property.Key, StringComparer.Ordinal))
        {
            normalized[property.Key] = property.Value is null
                ? null
                : NormalizeNode(property.Value);
        }

        return normalized;
    }

    private static JsonArray NormalizeArray(JsonArray jsonArray)
    {
        var normalized = new JsonArray();

        foreach (var item in jsonArray)
        {
            normalized.Add(item is null ? null : NormalizeNode(item));
        }

        return normalized;
    }
}
```

## Catatan Security/Consistency

Hash dihitung dari JSON yang dinormalisasi:

```text
- Object property diurutkan by key.
- Whitespace tidak berpengaruh.
- Array order tetap dipertahankan.
- SHA-256 dipakai untuk deterministic payload fingerprint.
```

Jadi payload ini dianggap sama:

```json
{"customerId":"x","items":[]}
```

dan:

```json
{
  "items": [],
  "customerId": "x"
}
```

***

# 4. Idempotency Service

## `IdempotencyService.cs`

Replace:

```text
src/OrderManagement.Infrastructure/Idempotency/IdempotencyService.cs
```

Isi:

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderManagement.Application.Abstractions.Idempotency;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.DTOs.Idempotency;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Enums;
using OrderManagement.Infrastructure.Options;

namespace OrderManagement.Infrastructure.Idempotency;

public sealed class IdempotencyService : IIdempotencyService
{
    private readonly IIdempotencyRepository _repository;
    private readonly IClock _clock;
    private readonly IdempotencyOptions _options;
    private readonly ILogger<IdempotencyService> _logger;

    public IdempotencyService(
        IIdempotencyRepository repository,
        IClock clock,
        IOptions<IdempotencyOptions> options,
        ILogger<IdempotencyService> logger)
    {
        _repository = repository;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IdempotencyProcessResult> BeginAsync(
        string key,
        Guid userId,
        string endpoint,
        string requestHash,
        CancellationToken cancellationToken = default)
    {
        ValidateBeginArguments(key, userId, endpoint, requestHash);

        var now = _clock.UtcNow;
        var lockedUntil = now.AddSeconds(_options.InProgressTtlSeconds);

        var request = new CreateIdempotencyRecordRequest
        {
            Key = key.Trim(),
            UserId = userId,
            Endpoint = endpoint.Trim(),
            RequestHash = requestHash,
            LockedUntil = lockedUntil,
            Now = now
        };

        var inserted = await _repository.TryInsertInProgressAsync(
            request,
            cancellationToken);

        if (inserted)
        {
            var insertedRecord = await _repository.GetByKeyAsync(
                userId,
                key,
                endpoint,
                cancellationToken);

            if (insertedRecord is null)
            {
                throw new InvalidOperationException("Inserted idempotency record cannot be found.");
            }

            _logger.LogInformation(
                "Idempotency key accepted for processing. UserId={UserId} Endpoint={Endpoint} IdempotencyKey={IdempotencyKey} RecordId={RecordId}",
                userId,
                endpoint,
                MaskKey(key),
                insertedRecord.Id);

            return IdempotencyProcessResult.ProcessRequest(insertedRecord.Id);
        }

        var existing = await _repository.GetByKeyAsync(
            userId,
            key,
            endpoint,
            cancellationToken);

        if (existing is null)
        {
            throw new InvalidOperationException("Idempotency insert conflicted but existing record cannot be found.");
        }

        if (existing.HasDifferentRequestHash(requestHash))
        {
            _logger.LogWarning(
                "Idempotency key reused with different payload. UserId={UserId} Endpoint={Endpoint} IdempotencyKey={IdempotencyKey}",
                userId,
                endpoint,
                MaskKey(key));

            throw ConflictAppException.IdempotencyKeyReusedWithDifferentPayload();
        }

        if (existing.Status == IdempotencyStatus.Completed)
        {
            if (existing.ResponseStatusCode is null || string.IsNullOrWhiteSpace(existing.ResponseBody))
            {
                throw new InvalidOperationException("Completed idempotency record does not have stored response.");
            }

            _logger.LogInformation(
                "Returning stored idempotent response. UserId={UserId} Endpoint={Endpoint} IdempotencyKey={IdempotencyKey} RecordId={RecordId}",
                userId,
                endpoint,
                MaskKey(key),
                existing.Id);

            return IdempotencyProcessResult.ReturnStoredResponse(
                existing.ResponseStatusCode.Value,
                existing.ResponseBody);
        }

        if (existing.Status == IdempotencyStatus.Failed)
        {
            if (existing.ResponseStatusCode is not null && !string.IsNullOrWhiteSpace(existing.ResponseBody))
            {
                _logger.LogInformation(
                    "Returning stored failed idempotent response. UserId={UserId} Endpoint={Endpoint} IdempotencyKey={IdempotencyKey} RecordId={RecordId}",
                    userId,
                    endpoint,
                    MaskKey(key),
                    existing.Id);

                return IdempotencyProcessResult.ReturnStoredResponse(
                    existing.ResponseStatusCode.Value,
                    existing.ResponseBody);
            }

            _logger.LogWarning(
                "Idempotency record is Failed without stored response. UserId={UserId} Endpoint={Endpoint} IdempotencyKey={IdempotencyKey} RecordId={RecordId}",
                userId,
                endpoint,
                MaskKey(key),
                existing.Id);

            throw ConflictAppException.RequestAlreadyInProgress();
        }

        if (existing.IsInProgress(now))
        {
            _logger.LogInformation(
                "Idempotency request already in progress. UserId={UserId} Endpoint={Endpoint} IdempotencyKey={IdempotencyKey} RecordId={RecordId}",
                userId,
                endpoint,
                MaskKey(key),
                existing.Id);

            throw ConflictAppException.RequestAlreadyInProgress();
        }

        _logger.LogWarning(
            "Idempotency key has stale InProgress record. UserId={UserId} Endpoint={Endpoint} IdempotencyKey={IdempotencyKey} RecordId={RecordId}",
            userId,
            endpoint,
            MaskKey(key),
            existing.Id);

        throw ConflictAppException.RequestAlreadyInProgress();
    }

    public async Task MarkCompletedAsync(
        Guid recordId,
        int responseStatusCode,
        string responseBody,
        string resourceType,
        Guid resourceId,
        CancellationToken cancellationToken = default)
    {
        if (recordId == Guid.Empty)
        {
            throw new ArgumentException("Idempotency record id is required.", nameof(recordId));
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            throw new ArgumentException("Response body is required.", nameof(responseBody));
        }

        if (string.IsNullOrWhiteSpace(resourceType))
        {
            throw new ArgumentException("Resource type is required.", nameof(resourceType));
        }

        if (resourceId == Guid.Empty)
        {
            throw new ArgumentException("Resource id is required.", nameof(resourceId));
        }

        await _repository.MarkCompletedAsync(
            recordId,
            responseStatusCode,
            responseBody,
            resourceType,
            resourceId,
            _clock.UtcNow,
            cancellationToken);
    }

    public async Task MarkFailedAsync(
        Guid recordId,
        int responseStatusCode,
        string responseBody,
        CancellationToken cancellationToken = default)
    {
        if (recordId == Guid.Empty)
        {
            throw new ArgumentException("Idempotency record id is required.", nameof(recordId));
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            throw new ArgumentException("Response body is required.", nameof(responseBody));
        }

        await _repository.MarkFailedAsync(
            recordId,
            responseStatusCode,
            responseBody,
            _clock.UtcNow,
            cancellationToken);
    }

    private void ValidateBeginArguments(
        string key,
        Guid userId,
        string endpoint,
        string requestHash)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw ConflictAppException.RequestAlreadyInProgress();
        }

        if (key.Length > _options.KeyMaxLength)
        {
            throw new ValidationAppException(
                "Idempotency key validation failed.",
                [AppErrorDetail.ForField("Idempotency-Key", $"Idempotency key cannot be longer than {_options.KeyMaxLength} characters.")]);
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Endpoint is required.", nameof(endpoint));
        }

        if (string.IsNullOrWhiteSpace(requestHash))
        {
            throw new ArgumentException("Request hash is required.", nameof(requestHash));
        }
    }

    private static string MaskKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var trimmed = key.Trim();

        return trimmed.Length <= 8
            ? "********"
            : $"{trimmed[..8]}...";
    }
}
```

## Catatan kecil

Untuk `key` kosong, nanti normalnya sudah ditangkap oleh `RequireIdempotencyKeyFilter` dengan error `IDEMPOTENCY_KEY_REQUIRED`. Jadi di service ini mostly defensive.

***

# 5. Idempotency Repository with Dapper

## `IdempotencyRepository.cs`

Replace:

```text
src/OrderManagement.Infrastructure/Repositories/IdempotencyRepository.cs
```

Isi:

```csharp
using Dapper;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.DTOs.Idempotency;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Infrastructure.Repositories;

public sealed class IdempotencyRepository : IIdempotencyRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public IdempotencyRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> TryInsertInProgressAsync(
        CreateIdempotencyRecordRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           INSERT INTO idempotency_keys
                               (key, user_id, endpoint, request_hash, status, locked_until, created_at, updated_at)
                           VALUES
                               (@Key, @UserId, @Endpoint, @RequestHash, @Status, @LockedUntil, @Now, @Now)
                           ON CONFLICT (user_id, key, endpoint) DO NOTHING;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    request.Key,
                    request.UserId,
                    request.Endpoint,
                    request.RequestHash,
                    Status = IdempotencyStatus.InProgress.ToString(),
                    request.LockedUntil,
                    request.Now
                },
                cancellationToken: cancellationToken));

        return affectedRows == 1;
    }

    public async Task<IdempotencyRecord?> GetByKeyAsync(
        Guid userId,
        string key,
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT
                               id AS Id,
                               key AS Key,
                               user_id AS UserId,
                               endpoint AS Endpoint,
                               request_hash AS RequestHash,
                               status AS Status,
                               response_status_code AS ResponseStatusCode,
                               response_body::text AS ResponseBody,
                               resource_type AS ResourceType,
                               resource_id AS ResourceId,
                               locked_until AS LockedUntil,
                               created_at AS CreatedAt,
                               updated_at AS UpdatedAt
                           FROM idempotency_keys
                           WHERE user_id = @UserId
                             AND key = @Key
                             AND endpoint = @Endpoint
                           LIMIT 1;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<IdempotencyRecordRow>(
            new CommandDefinition(
                sql,
                new
                {
                    UserId = userId,
                    Key = key.Trim(),
                    Endpoint = endpoint.Trim()
                },
                cancellationToken: cancellationToken));

        return row?.ToDomain();
    }

    public async Task MarkCompletedAsync(
        Guid recordId,
        int responseStatusCode,
        string responseBody,
        string resourceType,
        Guid resourceId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           UPDATE idempotency_keys
                           SET
                               status = @Status,
                               response_status_code = @ResponseStatusCode,
                               response_body = CAST(@ResponseBody AS jsonb),
                               resource_type = @ResourceType,
                               resource_id = @ResourceId,
                               locked_until = NULL,
                               updated_at = @Now
                           WHERE id = @RecordId
                             AND status = @ExpectedStatus;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    RecordId = recordId,
                    Status = IdempotencyStatus.Completed.ToString(),
                    ExpectedStatus = IdempotencyStatus.InProgress.ToString(),
                    ResponseStatusCode = responseStatusCode,
                    ResponseBody = responseBody,
                    ResourceType = resourceType,
                    ResourceId = resourceId,
                    Now = now
                },
                cancellationToken: cancellationToken));

        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Failed to mark idempotency record '{recordId}' as completed. Record may not be in InProgress state.");
        }
    }

    public async Task MarkFailedAsync(
        Guid recordId,
        int responseStatusCode,
        string responseBody,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           UPDATE idempotency_keys
                           SET
                               status = @Status,
                               response_status_code = @ResponseStatusCode,
                               response_body = CAST(@ResponseBody AS jsonb),
                               locked_until = NULL,
                               updated_at = @Now
                           WHERE id = @RecordId
                             AND status = @ExpectedStatus;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    RecordId = recordId,
                    Status = IdempotencyStatus.Failed.ToString(),
                    ExpectedStatus = IdempotencyStatus.InProgress.ToString(),
                    ResponseStatusCode = responseStatusCode,
                    ResponseBody = responseBody,
                    Now = now
                },
                cancellationToken: cancellationToken));

        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Failed to mark idempotency record '{recordId}' as failed. Record may not be in InProgress state.");
        }
    }

    private sealed class IdempotencyRecordRow
    {
        public Guid Id { get; init; }

        public string Key { get; init; } = string.Empty;

        public Guid UserId { get; init; }

        public string Endpoint { get; init; } = string.Empty;

        public string RequestHash { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public int? ResponseStatusCode { get; init; }

        public string? ResponseBody { get; init; }

        public string? ResourceType { get; init; }

        public Guid? ResourceId { get; init; }

        public DateTimeOffset? LockedUntil { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public IdempotencyRecord ToDomain()
        {
            if (!Enum.TryParse<IdempotencyStatus>(Status, ignoreCase: true, out var parsedStatus))
            {
                throw new InvalidOperationException($"Invalid idempotency status value '{Status}' in database.");
            }

            return IdempotencyRecord.Rehydrate(
                Id,
                Key,
                UserId,
                Endpoint,
                RequestHash,
                parsedStatus,
                ResponseStatusCode,
                ResponseBody,
                ResourceType,
                ResourceId,
                LockedUntil,
                CreatedAt,
                UpdatedAt);
        }
    }
}
```

***

# 6. Infrastructure DI Update

## `DependencyInjection.cs`

Replace:

```text
src/OrderManagement.Infrastructure/DependencyInjection.cs
```

Isi:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Idempotency;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Rules;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Infrastructure.Database;
using OrderManagement.Infrastructure.Idempotency;
using OrderManagement.Infrastructure.Options;
using OrderManagement.Infrastructure.Repositories;
using OrderManagement.Infrastructure.Rules;
using OrderManagement.Infrastructure.Security;
using OrderManagement.Infrastructure.Time;

namespace OrderManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(
            configuration.GetSection(DatabaseOptions.SectionName));

        services.Configure<MigrationOptions>(
            configuration.GetSection(MigrationOptions.SectionName));

        services.Configure<JwtOptions>(
            configuration.GetSection(JwtOptions.SectionName));

        services.Configure<IdempotencyOptions>(
            configuration.GetSection(IdempotencyOptions.SectionName));

        services.AddHttpContextAccessor();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();

        services.AddScoped<IDatabaseMigrationRunner, DatabaseMigrationRunner>();

        services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();

        services.AddSingleton<IRequestHashService, RequestHashService>();
        services.AddScoped<IIdempotencyService, IdempotencyService>();

        services.AddSingleton<IOrderRulesService, NRulesOrderRulesService>();

        return services;
    }
}
```

***

# 7. RequireIdempotencyKeyFilter

Filter ini akan kita pakai nanti di `POST /api/v1/orders`.

## `RequireIdempotencyKeyFilter.cs`

Replace:

```text
src/OrderManagement.Api/Filters/RequireIdempotencyKeyFilter.cs
```

Isi:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using OrderManagement.Api.Contracts.Common;
using OrderManagement.Api.Middleware;
using OrderManagement.Application.Constants;
using OrderManagement.Infrastructure.Options;

namespace OrderManagement.Api.Filters;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireIdempotencyKeyFilter : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var options = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<IdempotencyOptions>>()
            .Value;

        var headerName = string.IsNullOrWhiteSpace(options.HeaderName)
            ? "Idempotency-Key"
            : options.HeaderName;

        if (!context.HttpContext.Request.Headers.TryGetValue(headerName, out var values) ||
            string.IsNullOrWhiteSpace(values.FirstOrDefault()))
        {
            context.Result = BuildErrorResult(
                context,
                StatusCodes.Status400BadRequest,
                ErrorCodes.IdempotencyKeyRequired,
                $"{headerName} header is required.");

            return;
        }

        var key = values.FirstOrDefault()!.Trim();

        if (key.Length > options.KeyMaxLength)
        {
            context.Result = BuildErrorResult(
                context,
                StatusCodes.Status422UnprocessableEntity,
                ErrorCodes.ValidationError,
                $"{headerName} header cannot be longer than {options.KeyMaxLength} characters.",
                new ApiErrorDetail(
                    headerName,
                    "Idempotency key is too long.",
                    new
                    {
                        maxLength = options.KeyMaxLength
                    }));

            return;
        }

        await next();
    }

    private static ObjectResult BuildErrorResult(
        ActionExecutingContext context,
        int statusCode,
        string code,
        string message,
        ApiErrorDetail? detail = null)
    {
        var correlationId = GetCorrelationId(context.HttpContext);

        var response = new ApiErrorResponse
        {
            Error = new ApiError
            {
                Code = code,
                Message = message,
                Details = detail is null ? [] : [detail],
                CorrelationId = correlationId,
                Timestamp = DateTimeOffset.UtcNow
            }
        };

        context.HttpContext.Response.Headers[CorrelationIdConstants.HeaderName] = correlationId;

        return new ObjectResult(response)
        {
            StatusCode = statusCode
        };
    }

    private static string GetCorrelationId(HttpContext context)
    {
        if (context.Items.TryGetValue(CorrelationIdConstants.HttpContextItemName, out var value) &&
            value is string correlationId &&
            !string.IsNullOrWhiteSpace(correlationId))
        {
            return correlationId;
        }

        if (context.Request.Headers.TryGetValue(CorrelationIdConstants.HeaderName, out var values))
        {
            var headerValue = values.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                return headerValue.Trim();
            }
        }

        return Guid.NewGuid().ToString("N");
    }
}
```

***

# 8. Helper untuk Ambil Idempotency Header

Biar controller/service nanti tidak copy-paste.

Create file:

```text
src/OrderManagement.Api/Extensions/HttpRequestIdempotencyExtensions.cs
```

Isi:

```csharp
using Microsoft.Extensions.Options;
using OrderManagement.Infrastructure.Options;

namespace OrderManagement.Api.Extensions;

public static class HttpRequestIdempotencyExtensions
{
    public static string GetRequiredIdempotencyKey(this HttpRequest request)
    {
        var options = request.HttpContext.RequestServices
            .GetRequiredService<IOptions<IdempotencyOptions>>()
            .Value;

        var headerName = string.IsNullOrWhiteSpace(options.HeaderName)
            ? "Idempotency-Key"
            : options.HeaderName;

        if (!request.Headers.TryGetValue(headerName, out var values))
        {
            throw new InvalidOperationException($"{headerName} header was not found.");
        }

        var key = values.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException($"{headerName} header was empty.");
        }

        return key.Trim();
    }
}
```

***

# 9. Endpoint Normalization Helper

Untuk idempotency scope, endpoint string harus konsisten. Nanti `POST /orders` bisa pakai helper ini.

Create file:

```text
src/OrderManagement.Api/Extensions/HttpRequestEndpointExtensions.cs
```

Isi:

```csharp
namespace OrderManagement.Api.Extensions;

public static class HttpRequestEndpointExtensions
{
    public static string GetNormalizedEndpoint(this HttpRequest request)
    {
        var method = request.Method.ToUpperInvariant();
        var path = request.Path.Value?.Trim().ToLowerInvariant() ?? string.Empty;

        return $"{method} {path}";
    }
}
```

***

# 10. Unit Tests untuk Request Hash

Create folder:

```bash
mkdir -p tests/OrderManagement.Tests/Infrastructure/Idempotency
```

Create file:

```text
tests/OrderManagement.Tests/Infrastructure/Idempotency/RequestHashServiceTests.cs
```

Isi:

```csharp
using FluentAssertions;
using OrderManagement.Infrastructure.Idempotency;

namespace OrderManagement.Tests.Infrastructure.Idempotency;

public sealed class RequestHashServiceTests
{
    private readonly RequestHashService _service = new();

    [Fact]
    public void ComputeHashFromJson_ShouldReturnSameHash_WhenObjectPropertyOrderDiffers()
    {
        const string json1 = """
                             {
                               "customerId": "customer-1",
                               "shippingAddress": "Address",
                               "items": [
                                 {
                                   "productId": "product-1",
                                   "quantity": 10
                                 }
                               ]
                             }
                             """;

        const string json2 = """
                             {
                               "items": [
                                 {
                                   "quantity": 10,
                                   "productId": "product-1"
                                 }
                               ],
                               "shippingAddress": "Address",
                               "customerId": "customer-1"
                             }
                             """;

        var hash1 = _service.ComputeHashFromJson(json1);
        var hash2 = _service.ComputeHashFromJson(json2);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeHashFromJson_ShouldReturnDifferentHash_WhenArrayOrderDiffers()
    {
        const string json1 = """
                             {
                               "items": [
                                 { "productId": "product-1", "quantity": 10 },
                                 { "productId": "product-2", "quantity": 5 }
                               ]
                             }
                             """;

        const string json2 = """
                             {
                               "items": [
                                 { "productId": "product-2", "quantity": 5 },
                                 { "productId": "product-1", "quantity": 10 }
                               ]
                             }
                             """;

        var hash1 = _service.ComputeHashFromJson(json1);
        var hash2 = _service.ComputeHashFromJson(json2);

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeHashFromJson_ShouldReturnDifferentHash_WhenQuantityDiffers()
    {
        const string json1 = """
                             {
                               "items": [
                                 { "productId": "product-1", "quantity": 10 }
                               ]
                             }
                             """;

        const string json2 = """
                             {
                               "items": [
                                 { "productId": "product-1", "quantity": 11 }
                               ]
                             }
                             """;

        var hash1 = _service.ComputeHashFromJson(json1);
        var hash2 = _service.ComputeHashFromJson(json2);

        hash1.Should().NotBe(hash2);
    }
}
```

***

# 11. Unit Tests untuk IdempotencyService

Untuk test service, kita butuh fake repo/clock simple.

Create folder:

```bash
mkdir -p tests/OrderManagement.Tests/Infrastructure/Idempotency
```

Create file:

```text
tests/OrderManagement.Tests/Infrastructure/Idempotency/IdempotencyServiceTests.cs
```

Isi:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.DTOs.Idempotency;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;
using OrderManagement.Infrastructure.Idempotency;
using OrderManagement.Infrastructure.Options;

namespace OrderManagement.Tests.Infrastructure.Idempotency;

public sealed class IdempotencyServiceTests
{
    [Fact]
    public async Task BeginAsync_ShouldReturnProcessRequest_WhenKeyIsNew()
    {
        var repository = new FakeIdempotencyRepository();
        var clock = new FakeClock(DateTimeOffset.UtcNow);

        var service = CreateService(repository, clock);

        var result = await service.BeginAsync(
            "key-1",
            Guid.NewGuid(),
            "POST /api/v1/orders",
            "hash-1");

        result.ShouldProcess.Should().BeTrue();
        result.RecordId.Should().NotBeNull();
    }

    [Fact]
    public async Task BeginAsync_ShouldThrowConflict_WhenSameKeyDifferentPayload()
    {
        var userId = Guid.NewGuid();
        var repository = new FakeIdempotencyRepository();
        var clock = new FakeClock(DateTimeOffset.UtcNow);

        var service = CreateService(repository, clock);

        await service.BeginAsync(
            "key-1",
            userId,
            "POST /api/v1/orders",
            "hash-1");

        var act = async () => await service.BeginAsync(
            "key-1",
            userId,
            "POST /api/v1/orders",
            "hash-2");

        var exception = await act.Should().ThrowAsync<ConflictAppException>();
        exception.Which.Code.Should().Be("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD");
    }

    [Fact]
    public async Task BeginAsync_ShouldThrowConflict_WhenSameKeyStillInProgress()
    {
        var userId = Guid.NewGuid();
        var repository = new FakeIdempotencyRepository();
        var clock = new FakeClock(DateTimeOffset.UtcNow);

        var service = CreateService(repository, clock);

        await service.BeginAsync(
            "key-1",
            userId,
            "POST /api/v1/orders",
            "hash-1");

        var act = async () => await service.BeginAsync(
            "key-1",
            userId,
            "POST /api/v1/orders",
            "hash-1");

        var exception = await act.Should().ThrowAsync<ConflictAppException>();
        exception.Which.Code.Should().Be("REQUEST_ALREADY_IN_PROGRESS");
    }

    [Fact]
    public async Task BeginAsync_ShouldReturnStoredResponse_WhenCompleted()
    {
        var userId = Guid.NewGuid();
        var repository = new FakeIdempotencyRepository();
        var clock = new FakeClock(DateTimeOffset.UtcNow);

        var service = CreateService(repository, clock);

        var begin = await service.BeginAsync(
            "key-1",
            userId,
            "POST /api/v1/orders",
            "hash-1");

        await service.MarkCompletedAsync(
            begin.RecordId!.Value,
            201,
            """{"id":"order-1"}""",
            "Order",
            Guid.NewGuid());

        var replay = await service.BeginAsync(
            "key-1",
            userId,
            "POST /api/v1/orders",
            "hash-1");

        replay.HasStoredResponse.Should().BeTrue();
        replay.StoredStatusCode.Should().Be(201);
        replay.StoredResponseBody.Should().Be("""{"id":"order-1"}""");
    }

    private static IdempotencyService CreateService(
        IIdempotencyRepository repository,
        IClock clock)
    {
        var options = Options.Create(new IdempotencyOptions
        {
            HeaderName = "Idempotency-Key",
            KeyMaxLength = 200,
            InProgressTtlSeconds = 120,
            CompletedRecordRetentionDays = 7,
            FailedRecordRetentionDays = 1
        });

        return new IdempotencyService(
            repository,
            clock,
            options,
            NullLogger<IdempotencyService>.Instance);
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class FakeIdempotencyRepository : IIdempotencyRepository
    {
        private readonly Dictionary<string, IdempotencyRecord> _records = [];

        public Task<bool> TryInsertInProgressAsync(
            CreateIdempotencyRecordRequest request,
            CancellationToken cancellationToken = default)
        {
            var dictionaryKey = BuildKey(request.UserId, request.Key, request.Endpoint);

            if (_records.ContainsKey(dictionaryKey))
            {
                return Task.FromResult(false);
            }

            var record = IdempotencyRecord.CreateInProgress(
                request.Key,
                request.UserId,
                request.Endpoint,
                request.RequestHash,
                request.LockedUntil,
                request.Now);

            _records[dictionaryKey] = record;

            return Task.FromResult(true);
        }

        public Task<IdempotencyRecord?> GetByKeyAsync(
            Guid userId,
            string key,
            string endpoint,
            CancellationToken cancellationToken = default)
        {
            _records.TryGetValue(BuildKey(userId, key, endpoint), out var record);

            return Task.FromResult(record);
        }

        public Task MarkCompletedAsync(
            Guid recordId,
            int responseStatusCode,
            string responseBody,
            string resourceType,
            Guid resourceId,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            var record = _records.Values.Single(x => x.Id == recordId);
            record.MarkCompleted(responseStatusCode, responseBody, resourceType, resourceId, now);

            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(
            Guid recordId,
            int responseStatusCode,
            string responseBody,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            var record = _records.Values.Single(x => x.Id == recordId);
            record.MarkFailed(responseStatusCode, responseBody, now);

            return Task.CompletedTask;
        }

        private static string BuildKey(Guid userId, string key, string endpoint)
        {
            return $"{userId:N}|{key.Trim()}|{endpoint.Trim()}";
        }
    }
}
```

***

# 12. Build & Test

Run:

```bash
dotnet build
```

Lalu:

```bash
dotnet test tests/OrderManagement.Tests/OrderManagement.Tests.csproj
```

Atau semua:

```bash
dotnet test
```

***

# 13. Production Notes Penting

Dengan Batch 7 ini, kita sudah punya jawaban kuat untuk idempotency:

```text
1. Unique constraint di DB mencegah race same key.
2. Request hash mencegah key sama dipakai untuk payload berbeda.
3. Completed response disimpan sebagai JSONB dan bisa direplay.
4. InProgress response mengembalikan 409 agar client tidak membuat order ganda.
5. Idempotency scoped by user + key + endpoint.
6. Key tidak dilog full, hanya prefix.
7. Hash payload deterministic dengan normalized JSON.
8. Repository update state hanya dari InProgress ke Completed/Failed.
```

Ini yang nanti dipakai di Batch 8:

```text
POST /api/v1/orders
- [RequireIdempotencyKeyFilter]
- get current user
- compute payload hash
- begin idempotency
- if stored response return replay
- if process request create order transaction
- serialize response
- mark idempotency completed
```

***

# 14. Commit Batch 7

```bash
git add .
git commit -m "feat: add idempotency service and request hashing"
```

***

