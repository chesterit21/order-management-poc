# Hardening Notes

## Order Number

Order number is generated using PostgreSQL sequence:

```text
ORD-yyyyMMdd-sequence
```

This avoids random collision risk and is safe under concurrent requests.

## Idempotency

Current implementation inserts idempotency record before order processing and stores completed response after successful order creation.

The design protects against duplicate order creation for the same key through:

```text
UNIQUE (user_id, key, endpoint)
```

Future enhancement:

```text
Use shared UnitOfWork transaction so idempotency Begin, order creation, and MarkCompleted commit atomically.
```

## Cancellation Stock Restore

Cancellation reason controls stock behavior.

```text
StockUnavailable and InventoryMismatch do not restore stock.
```

This protects against overstating stock when physical stock was already sold offline or warehouse stock is mismatched.

## Payment

Payment and cancel both lock order row with FOR UPDATE. This serializes race between payment success and cancellation.

## Security

Current POC uses JWT access token only. Refresh token, account lockout, and external identity provider integration are out of scope.