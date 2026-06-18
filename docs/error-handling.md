# Error Handling Design

## 1. Purpose

Error handling di API ini dibuat agar client mendapatkan response yang konsisten, aman, dan mudah dipakai untuk UX handling.

Goals:

```text
- Semua error punya format sama.
- Correlation ID selalu ada.
- Stack trace tidak keluar ke client.
- Status code sesuai jenis error.
- Sensitive data tidak bocor.
```

## 2. Standard Error Response

Format:

```json
{
  "error": {
    "code": "ERROR_CODE",
    "message": "Human readable message.",
    "details": [
      {
        "field": "items[0].quantity",
        "message": "Requested quantity exceeds available stock.",
        "metadata": {
          "requestedQuantity": 10,
          "availableQuantity": 5
        }
      }
    ],
    "correlationId": "trace-id",
    "timestamp": "2026-06-17T00:00:00Z"
  }
}
```

## 3. Exception Types

Application exception hierarchy:

```text
AppException
├── ValidationAppException
├── NotFoundAppException
├── ConflictAppException
│   └── IdempotencyConflictAppException
├── BusinessRuleAppException
├── UnauthorizedAppException
├── ForbiddenAppException
├── ConcurrencyAppException
├── DuplicatePaymentAppException
└── InvalidOrderStatusAppException
```

Global exception middleware menangkap semua exception dan mengubahnya menjadi `ApiErrorResponse`.

## 4. HTTP Status Mapping

```text
400 Bad Request
  Missing required header, malformed HTTP-level request.

401 Unauthorized
  Missing token, invalid token, invalid credentials.

403 Forbidden
  User authenticated but not allowed.

404 Not Found
  Entity does not exist or not accessible.

409 Conflict
  Idempotency conflict, stock conflict, row version conflict.

422 Unprocessable Entity
  Business validation failed, invalid transition, payment not allowed.

499 Client Closed Request
  OperationCanceledException — client disconnected or request cancelled.

500 Internal Server Error
  Unexpected error.
```

## 5. Main Error Codes

Validation/auth:

```text
VALIDATION_ERROR
UNAUTHORIZED
FORBIDDEN
INVALID_CREDENTIALS
USER_INACTIVE
```

Entity:

```text
USER_NOT_FOUND
PRODUCT_NOT_FOUND
ORDER_NOT_FOUND
PAYMENT_NOT_FOUND
```

Order/stock:

```text
INSUFFICIENT_STOCK
INVALID_ORDER_STATUS_TRANSITION
ORDER_ALREADY_CANCELLED
ORDER_TERMINAL_STATE
CONCURRENT_UPDATE_CONFLICT
INVALID_CANCELLATION_REASON
CANCELLED_STATUS_REQUIRES_CANCEL_ENDPOINT
```

Idempotency:

```text
IDEMPOTENCY_KEY_REQUIRED
REQUEST_ALREADY_IN_PROGRESS
IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD
```

Payment:

```text
PAYMENT_ALREADY_PAID
PAYMENT_NOT_ALLOWED
```

Database/internal:

```text
DATABASE_CONSTRAINT_VIOLATION
INTERNAL_SERVER_ERROR
```

## 6. PostgreSQL Exception Handling

`PostgresException` dimapping berdasarkan SqlState:

```text
23505 (UniqueViolation)
  → 409 Conflict — Duplicate data violates a unique database constraint.

23503 (ForeignKeyViolation)
  → 409 Conflict — Referenced data does not exist.

23514 (CheckViolation)
  → 422 Unprocessable Entity — Data violates a database check constraint.

40001 (SerializationFailure)
  → 409 Conflict — Transaction serialization failure.

55P03 (LockNotAvailable)
  → 409 Conflict — Lock timeout exceeded.
```

Response tidak menampilkan raw SQL atau stack trace.

## 7. OperationCanceledException Handling

`GlobalExceptionHandlingMiddleware` juga menangkap `OperationCanceledException` dan mengembalikan:

```text
499 Client Closed Request
  Client disconnected or request was cancelled (e.g. timeout, user aborts).
```

Ini mencegah log error yang tidak perlu saat client memutus koneksi.

## 8. Security Rules

Tidak boleh ditampilkan di error response:

```text
Password
Password hash
JWT token
Authorization header
Connection string
Raw SQL with parameters
Stack trace
File path internal
```

## 9. Client UX Recommendations

### INSUFFICIENT_STOCK

Client should:

```text
- Show latest available quantity.
- Refresh product stock.
- Ask user to adjust quantity.
- Generate new idempotency key for next changed payload.
```

### CONCURRENT_UPDATE_CONFLICT

Client should:

```text
- Refresh order detail.
- Show latest rowVersion/status.
- Ask user to retry intentionally.
```

### REQUEST_ALREADY_IN_PROGRESS

Client should:

```text
- Show previous request is still being processed.
- Disable duplicate submit.
- Retry with same key after delay if needed.
```

### IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD

Client should:

```text
- Generate new key because payload changed.
```

## 10. Logging Integration

Every error is logged with:

```text
CorrelationId
ErrorCode
StatusCode
Method
Path
ExceptionType
```

Activity log event `RequestFailed` is also emitted asynchronously for traceability.
