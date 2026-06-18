using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.DTOs.Idempotency;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Entities;
using OrderManagement.Infrastructure.Idempotency;

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

        var exception = await act.Should().ThrowAsync<IdempotencyConflictException>();
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

        var exception = await act.Should().ThrowAsync<IdempotencyConflictException>();
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
        return new IdempotencyService(
            repository,
            clock,
            NullLogger<IdempotencyService>.Instance,
            Mock.Of<IActivityLogWriter>());
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