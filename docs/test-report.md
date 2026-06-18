# Test Report Notes

## Unit Tests (OrderManagement.Tests)

**17 test files** across Domain, Application, and Infrastructure layers.

### Infrastructure

| Test File | Description |
|---|---|
| `IdempotencyRequestHashTests` | Ensures hash computation is deterministic, handles null/normalized inputs |
| `IdempotencyServiceTests` | Verifies duplicate detection, cache expiry, concurrent key checks |

### Domain (NRules)

| Test File | Description |
|---|---|
| `OrderTransitionRuleTests` | Validates all allowed/forbidden status transitions with `UserRole` |
| `OrderStatusTransitionTests` | Edge cases: same-status reentry, invalid jumps, role-based guards |
| `CancelOrderRuleTests` | Cancel eligibility: only cancellable statuses, Buyer vs Admin rules |
| `PaymentRuleTests` | Payment eligibility per status and role (Buyer, SellerAdmin, ApplicationAdmin) |

### Application — Validators (FluentValidation)

| Test File | Description |
|---|---|
| `CreateOrderCommandValidatorTests` | Required fields (Buyer id, product, quantity), boundary values |
| `CancelOrderCommandValidatorTests` | Reason required, valid enum values, order id format |
| `UpdateOrderStatusCommandValidatorTests` | Status enum validation, new-status vs current-status rules |
| `LoginCommandValidatorTests` | Username/password required, format constraints |

### Application — Services

| Test File | Description |
|---|---|
| `OrderServiceTests` | Create/cancel/update with role checks (Buyer, SellerAdmin), stock validation |
| `OrderCancellationPolicyTests` | Business rules: Buyer can cancel with `CustomerRequested` reason only; Admin has broader access |
| `DemoServiceTests` | Concurrent stock deduction orchestrator, idempotent-race coordinator |

### Test Infrastructure

| Library | Usage |
|---|---|
| **xUnit** | Test framework |
| **AutoFixture** | Auto-generate test data (orders, products, users) |
| **Moq** | Mock `IOrderRepository`, `ICurrentUserContext`, `IIdempotencyService` |
| **FluentAssertions** | Readable assertions on status, stock, error messages |

---

## Integration Tests (OrderManagement.IntegrationTests)

**20 test files** running against a real PostgreSQL via Testcontainers.

### Concurrency

| Test File | Description |
|---|---|
| `ConcurrentStockDeductionTests` | Two simultaneous buys from limited stock |
| `IdempotentCreateRaceTests` | Two POSTs with same `Idempotency-Key` |
| `ConcurrentStatusUpdateTests` | Admin A ships while Admin B cancels |
| `PaymentVsCancelRaceTests` | Payment and cancel arrive at the same time |
| `DuplicatePaymentPreventionTests` | Second payment for the same order is rejected |

### API

| Test File | Description |
|---|---|
| `AuthApiTests` | Login success, bad credentials, token expiry |
| `OrderApiTests` | CRUD flows, role-based access, status transitions via HTTP |
| `ProductApiTests` | Product listing, stock visibility per role |
| `StoreApiTests` | Store creation, buyer-only endpoints |

### Infrastructure

| Test File | Description |
|---|---|
| `DatabaseMigrationTests` | Schema idempotency, rollback compatibility |
| `ActivityLogRepositoryTests` | Log write/read, filtering by entity type |

### Test Helpers

| Helper | Description |
|---|---|
| `CustomWebApplicationFactory` | Extends `WebApplicationFactory<Program>`, starts Testcontainers PostgreSQL, applies EF/Dapper migrations |
| `TestContainers setup` | `PostgreSqlBuilder` with `postgres:17-alpine`, per-test-suite database lifecycle |

### Infrastructure Packages

| Library | Usage |
|---|---|
| **Testcontainers.PostgreSql** | Ephemeral PostgreSQL 17 container per test run |
| **Microsoft.AspNetCore.Mvc.Testing** | `WebApplicationFactory` for in-process API calls |
| **Dapper** | Direct SQL assertions on stock, order status, payment rows |
| **FluentAssertions** | Response body and DB state verification |
| **Npgsql** | ADO.NET provider for PostgreSQL |

---

## Key Assertions

### Concurrent Stock Deduction

Setup:

```text
Product stock = 15
Request A quantity = 10
Request B quantity = 10
```

Expected:

```text
Only one order succeeds
One order fails with conflict
Final stock = 5
Stock never negative
```

### Idempotent Create Race

Setup:

```text
Two POST /orders
Same user (Buyer)
Same Idempotency-Key
Same payload
```

Expected:

```text
Only one order created
Only one stock deduction
Second request gets either stored response or request-in-progress conflict
```

### Concurrent Status Update

Setup:

```text
Order Confirmed
Admin A -> Shipped
Admin B -> Cancel
```

Expected:

```text
Only one operation succeeds
Final status consistent
Loser receives conflict or business rule error
```

### Payment vs Cancel Race

Expected:

```text
Payment wins:
  order confirmed
  cancel may later mark payment refund required

Cancel wins:
  order cancelled
  payment rejected
```

### Duplicate Payment Prevention

Expected:

```text
Only one Paid payment exists
Order status Confirmed
Second payment rejected
```

### Authentication & Authorization

Expected:

```text
Login with valid credentials returns JWT token
Login with invalid credentials returns 401
Buyer cannot access Admin-only endpoints
SellerAdmin can manage orders but not stores
Requests without token return 401
```

### Order Lifecycle (API)

Expected:

```text
Buyer creates order -> stock deducted, status Pending
Buyer cancels own order with 'CustomerRequested' reason
Buyer cannot cancel with 'StockIssue' or other reasons
SellerAdmin transitions Pending -> Confirmed -> Shipped -> Delivered
Invalid transitions return 422 with business rule violation
```

### Idempotency (API)

Expected:

```text
POST with new Idempotency-Key creates resource and returns 201
Replay with same key returns same response (201) without side effects
Idempotency-Key persisted in database until TTL expiry
Concurrent identical keys resolve to single resource
```

---

## Production Notes

The tests demonstrate that the API protects critical sections through PostgreSQL row-level locking (`FOR UPDATE` / `SKIP LOCKED`), row version checks (`xmin`), unique constraints on idempotency keys, and NRules-based business rule enforcement. Concurrency tests use `Parallel.ForEachAsync` and `Task.WhenAll` to verify race-condition safety under load.
