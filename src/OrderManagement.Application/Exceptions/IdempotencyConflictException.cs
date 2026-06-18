using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Exceptions;

using OrderManagement.Application.Constants;

public sealed class IdempotencyConflictException : AppException
{
    public IdempotencyConflictType ConflictType { get; }

    public IdempotencyRecord? ExistingRecord { get; }

    public IdempotencyConflictException(
        string message,
        IdempotencyConflictType conflictType,
        IdempotencyRecord? existingRecord = null)
        : base(GetErrorCode(conflictType), message, StatusCodes.Conflict)
    {
        ConflictType = conflictType;
        ExistingRecord = existingRecord;
    }

    private static string GetErrorCode(IdempotencyConflictType conflictType) => conflictType switch
    {
        IdempotencyConflictType.InProgress => ErrorCodes.RequestAlreadyInProgress,
        IdempotencyConflictType.DifferentPayload => ErrorCodes.IdempotencyKeyReusedWithDifferentPayload,
        _ => ErrorCodes.IdempotencyKeyConflict
    };
}

public enum IdempotencyConflictType
{
    InProgress,
    DifferentPayload
}
