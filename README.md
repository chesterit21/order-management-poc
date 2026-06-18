# Order Management API POC

Prototype REST API for order management with idempotency, concurrency handling, PostgreSQL row locking, Dapper, NRules, JWT authentication, structured logging, and consistent error handling.

## Tech Stack

- ASP.NET Core API .NET 10
- PostgreSQL
- Dapper
- NRules
- JWT Bearer Authentication
- Serilog
- xUnit
- Testcontainers PostgreSQL

## Main Features

- Login with JWT
- Product list and detail
- Create order with stock deduction
- Idempotency-Key support for create order
- Get order detail
- List orders with filters and pagination
- Update order status
- Cancel order
- Conditional stock restore on cancel
- Mock payment flow
- Payment success confirms order
- Structured logging with correlation ID
- Consistent error response
- Database migration runner at startup
- Concurrency integration tests

## Order Lifecycle

Allowed transitions:

```text
Pending   -> Confirmed
Pending   -> Cancelled
Confirmed -> Shipped
Confirmed -> Cancelled
Shipped   -> Delivered
Delivered -> terminal
Cancelled -> terminal
```

Important:

```text
Cancelled status cannot be set through PATCH /status.
Use POST /cancel to ensure stock and audit trail are handled correctly.
```

## Cancellation Reasons

Supported cancellation reasons:

```text
CustomerRequested
StockUnavailable
InventoryMismatch
OperationalIssue
FraudSuspected
```

Stock restore behavior:

```text
CustomerRequested   -> restore stock
OperationalIssue    -> restore stock
FraudSuspected      -> restore stock
StockUnavailable    -> do not restore stock
InventoryMismatch   -> do not restore stock
```

This prevents system stock from being overstated when physical stock is unavailable due to offline/manual sales or warehouse mismatch.

## Idempotency Strategy

Create order requires:

```http
Idempotency-Key: {unique-key}
```

Behavior:

```text
New key + same user + same endpoint:
  process request

Same key + same payload + completed:
  return stored response

Same key + same payload + in progress:
  return 409 REQUEST_ALREADY_IN_PROGRESS

Same key + different payload:
  return 409 IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD
```

Uniqueness:

```sql
UNIQUE (user_id, key, endpoint)
```

Request hash:

```text
SHA-256 of normalized JSON payload
```

## Concurrency Strategy

### Stock Deduction

When creating order:

```sql
SELECT ...
FROM products
WHERE id = ANY(@ProductIds)
ORDER BY id
FOR UPDATE;
```

Then stock is validated and deducted in the same transaction.

Why:

```text
- Prevents stock from going negative
- Ensures latest stock is checked
- ORDER BY id reduces deadlock risk for multi-product orders
```

### Status Update

When updating order status:

```sql
SELECT ...
FROM orders
WHERE id = @OrderId
FOR UPDATE;
```

Then:

```text
- row_version is checked
- NRules validates latest transition
- status_history is inserted
```

### Cancel Order

Cancel order locks:

```text
1. order row
2. product rows ordered by product_id
```

Then:

```text
- validates cancel eligibility
- restores or does not restore stock based on cancellation reason
- inserts inventory movement
- inserts status history
```

### Payment vs Cancel Race

Payment and cancel both lock the same order row.

Possible outcomes:

```text
Payment wins:
  Pending -> Confirmed
  Cancel may still happen from Confirmed
  Paid payment is marked RefundRequired

Cancel wins:
  Pending -> Cancelled
  Payment is rejected
```

## Race Conditions Covered

1. Concurrent stock deduction
2. Concurrent status update
3. Idempotent create race
4. Double cancel
5. Payment vs cancel
6. Duplicate payment
7. Manual/offline stock mismatch cancellation

## Error Response Format

```json
{
  "error": {
    "code": "ERROR_CODE",
    "message": "Human readable message.",
    "details": [],
    "correlationId": "trace-id",
    "timestamp": "2026-06-17T06:00:00Z"
  }
}
```

## Logging

Every request supports:

```http
X-Correlation-ID: optional-client-correlation-id
```

If missing, API generates one.

Logs include:

```text
CorrelationId
UserId
Username
Role
RequestPath
HttpMethod
StatusCode
ElapsedMs
```

## Database Migration

Migrations are stored in:

```text
db/migrations
```

At application startup, API applies pending migrations and tracks checksum in:

```text
schema_migrations
```

If an applied migration file is modified, startup fails. Create a new migration instead.

## Local Run

### Option 1: Direct Project Run
Set PostgreSQL connection string in:

```text
src/OrderManagement.Api/appsettings.Development.json
```

Run:

```bash
dotnet restore
dotnet build
dotnet run --project src/OrderManagement.Api/OrderManagement.Api.csproj
```

### Option 2: Using Convenience Script (Recommended)
From the solution root directory, run:

```bash
./scripts/run-api.sh
```

### Option 3: Navigate to Project Directory
```bash
cd src/OrderManagement.Api
dotnet run
```

Swagger:

```text
/swagger
```

Health:

```text
/health
```

## Database Setup

### Using Convenience Script (Recommended)
From the solution root directory:

```bash
./scripts/reset-db.sh
```

### Manual Setup
Run seed manually:

```bash
PGPASSWORD=order_password psql -h localhost -p 5432 -U order_user -d order_management_test -f db/seed/001_seed_users.sql
PGPASSWORD=order_password psql -h localhost -p 5432 -U order_user -d order_management_test -f db/seed/002_seed_products.sql
```

Default users:

```text
appadmin   / Password123!  (ApplicationAdmin — full access)
devops     / Password123!  (DevOps — observability only)
selleradmin1 / Password123! (SellerAdmin — manages a store)
buyer1     / Password123!  (Buyer — can create orders)
buyer2     / Password123!  (Buyer — can create orders)
```

For backoffice testing, login as `appadmin` or `selleradmin1`.
For buyer flow testing, login as `buyer1` or `buyer2`.

## Run Tests

Unit tests:

```bash
dotnet test tests/OrderManagement.Tests/OrderManagement.Tests.csproj
```

Integration tests:

```bash
dotnet test tests/OrderManagement.IntegrationTests/OrderManagement.IntegrationTests.csproj
```

All tests:

```bash
dotnet test
```

Integration tests use Testcontainers PostgreSQL and require Docker engine running.

## Known Limitations

* Payment provider is mocked.
* Inventory service is embedded in order API for prototype.
* No distributed message broker.
* No outbox pattern.
* No refresh token.
* Idempotency and order creation are currently separate transactions. Future improvement is a shared UnitOfWork transaction for Begin/Create/MarkCompleted.