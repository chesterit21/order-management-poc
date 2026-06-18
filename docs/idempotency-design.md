# Idempotency Design

## 1. Purpose

Idempotency mencegah duplicate order ketika:

```text
- User double-click Submit.
- Client retry karena timeout.
- Network retry mengirim request yang sama.
- Dua request dengan key sama tiba bersamaan.
```

## 2. Header

Create order wajib mengirim:

```http
Idempotency-Key: {unique-key}
```

Recommended client key:

```text
UUID / crypto random id
```

## 3. Client Rules

Client harus:

```text
- Generate key saat user submit pertama kali.
- Reuse same key untuk retry exact same payload.
- Generate new key jika payload/cart berubah.
- Disable submit button saat request in-flight.
```

UI disable button hanya UX improvement. Backend idempotency tetap wajib.

## 4. Scope

Unique scope:

```text
user_id + key + endpoint
```

Kenapa:

```text
- Key user A tidak bentrok dengan user B.
- Key sama di endpoint berbeda tidak bentrok.
```

## 5. Request Hash

Backend menghitung hash:

```text
SHA-256(normalized JSON payload)
```

Normalization:

```text
- Object properties sorted by key.
- Whitespace ignored.
- Array order preserved.
```

Hash digunakan untuk mendeteksi:

```text
Same Idempotency-Key reused with different payload.
```

## 6. Database Table

Table:

```text
idempotency_keys
```

Important columns:

```text
key
user_id
endpoint
request_hash
status
response_status_code
response_body
resource_type
resource_id
locked_until
created_at
updated_at
```

Status:

```text
InProgress
Completed
Failed
```

Unique constraint:

```sql
UNIQUE (user_id, key, endpoint)
```

## 7. Flow

### New Request

```text
1. Compute request hash.
2. Insert idempotency record status InProgress.
3. If insert succeeds, process order.
4. Store response body and status code.
5. Mark Completed.
```

### Same Key Same Payload Completed

```text
Return stored response.
```

### Same Key Same Payload InProgress

```text
Return 409 REQUEST_ALREADY_IN_PROGRESS.
```

### Same Key Different Payload

```text
Return 409 IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD.
```

### Failed Previous Request

```text
Return stored failed response if available.
```

## 8. Race Handling

Two same-key requests arrive at same time:

```text
Request A INSERT InProgress succeeds.
Request B INSERT conflicts due unique constraint.
Request B reads existing record.
Request B returns InProgress conflict or stored response.
```

Expected:

```text
Only one order creation flow executes.
Only one stock deduction occurs.
```

## 9. Activity Logs

Idempotency emits activity log events:

```text
IdempotencyAccepted
IdempotencyReplayReturned
IdempotencyConflict
```

Metadata only stores safe data:

```text
endpoint
idempotencyKeyPrefix
recordId
resourceType
resourceId
reason
```

Full key is not logged.

## 10. Current Limitation

Current design:

```text
Idempotency Begin and MarkCompleted are outside order transaction.
```

It is still safe for duplicate prevention because unique constraint gates processing.

Future production improvement:

```text
Use shared UnitOfWork transaction:
Begin idempotency -> create order -> mark completed -> commit
```

## 11. What Must Not Happen

```text
- Same key creates two orders.
- Same key deducts stock twice.
- Same key different payload is accepted silently.
- Full idempotency key logged in activity logs.
```