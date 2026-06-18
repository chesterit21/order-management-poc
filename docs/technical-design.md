# Technical Design Document - Order Management API

## 1. Overview

Order Management API adalah prototype production-oriented untuk menangani order, stock, status, payment, idempotency, concurrency, dan tracing.

Masalah yang diselesaikan:

```text
- Double order karena double-click/retry.
- Stock minus karena concurrent order.
- Status order inconsistent karena concurrent admin update.
- Cancel order dengan stock restore/no-restore policy.
- Payment/cancel race.
- Minimnya logging dan tracing operational.
```

## 2. Technology Stack

```text
ASP.NET Core Web API .NET 10
PostgreSQL
Dapper
NRules
JWT Bearer Authentication
BCrypt.Net-Next
Serilog
xUnit
Testcontainers PostgreSQL
```

## 3. Solution Structure

```text
src/
  OrderManagement.Api
  OrderManagement.Application
  OrderManagement.Domain
  OrderManagement.Infrastructure

tests/
  OrderManagement.Tests
  OrderManagement.IntegrationTests

db/
  migrations
  seed

docs/
postman/
scripts/
```

## 4. Layer Responsibilities

### OrderManagement.Api

```text
Controllers
HTTP contracts
Middleware
Swagger setup
Authentication/authorization setup
CORS
Health check
Internal activity logs page/API
```

### OrderManagement.Application

```text
Use case services
DTOs/Commands/Results
Validators
Application exceptions
Interfaces/abstractions
Business orchestration
Authorization decisions based on current user
Cancellation policy
```

### OrderManagement.Domain

```text
Entities
Enums
Value objects
Rule facts
Rule results
Domain constants
```

### OrderManagement.Infrastructure

```text
Dapper repositories
PostgreSQL connection factory
Migration runner
JWT generator
BCrypt password hasher
Current user context
NRules implementation
Idempotency persistence
Request hashing
Activity log queue/background worker/repository
```

## 5. Main Modules

## 5.1 Authentication

Login flow:

```text
POST /api/v1/auth/login
Validate input
Find user
Verify BCrypt password
Generate JWT
Return token and user info
```

Security:

```text
Invalid username/password returns generic INVALID_CREDENTIALS.
Password is never logged.
JWT is never logged.
```

## 5.2 Products

Endpoints:

```text
GET /api/v1/products
GET /api/v1/products/{id}
```

Product fields:

```text
sku
name
stock_quantity
price
row_version
is_active
```

Stock is protected by:

```text
CHECK stock_quantity >= 0
row-level locking during order/cancel
```

## 5.3 Orders

Endpoints:

```text
POST /api/v1/orders
GET /api/v1/orders/{id}
GET /api/v1/orders
PATCH /api/v1/orders/{id}/status
POST /api/v1/orders/{id}/cancel
```

Order lifecycle:

```text
Pending -> Confirmed | Cancelled
Confirmed -> Shipped | Cancelled
Shipped -> Delivered
Delivered / Cancelled -> terminal
```

`Cancelled` is not allowed via generic status update. Cancel endpoint must be used.

## 5.4 Cancellation Policy

Cancellation reasons:

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
StockUnavailable    -> no restore
InventoryMismatch   -> no restore
```

Purpose:

```text
Avoid overstating system stock when physical stock is unavailable due offline/manual sale.
```

## 5.5 Payment

Endpoints:

```text
POST /api/v1/orders/{id}/payments
GET /api/v1/orders/{id}/payments
```

Payment flow:

```text
Pending order + payment success -> payment Paid + order Confirmed
Pending order + payment failed -> payment Failed + order remains Pending
Cancel paid order -> payment RefundRequired
```

## 5.6 Idempotency

Create order requires:

```http
Idempotency-Key
```

Table unique constraint:

```text
user_id + key + endpoint
```

Request hash:

```text
SHA-256 normalized JSON
```

## 5.7 Activity Logs

Activity logs are emitted through async queue and persisted by background worker.

Internal tracing:

```text
GET /api/v1/internal/activity-logs
GET /api/v1/internal/activity-logs/{id}
GET /internal/activity-logs
```

Admin/Ops only for data API.

## 6. Database Tables

```text
users
products
orders
order_items
inventory_movements
order_status_history
idempotency_keys
payments
activity_logs
schema_migrations
```

## 7. Migration Runner

At startup:

```text
1. Reads db/migrations.
2. Creates schema_migrations if missing.
3. Applies pending migrations in filename order.
4. Stores checksum.
5. Fails startup if applied migration checksum changed.
```

## 8. Critical Transactions

### Create Order

```text
Begin transaction
Generate order number from sequence
Lock products FOR UPDATE ORDER BY id
Validate stock
Insert order
Deduct stock
Insert inventory movement
Insert order items
Insert status history
Commit
```

### Update Status

```text
Begin transaction
Lock order FOR UPDATE
Check expectedRowVersion
Run NRules
Update status + row_version
Insert status history
Commit
```

### Cancel

```text
Begin transaction
Lock order FOR UPDATE
Check expectedRowVersion
Run NRules cancel
Lock product rows FOR UPDATE ORDER BY id
Restore or no-restore stock based on cancellation reason
Insert inventory movement
Mark paid payment RefundRequired if any
Update order Cancelled
Insert status history
Commit
```

### Payment

```text
Begin transaction
Lock order FOR UPDATE
Check permission
Check existing paid payment
Run NRules payment
Insert payment
If Paid: update order Confirmed and insert history
Commit
```

## 9. Security Design

```text
JWT bearer authentication
Role-based authorization
BCrypt password hashing
No token/password logging
Consistent error response without stack trace
Internal logs API Admin/Ops only
Customer data isolation
```

## 10. Observability

```text
X-Correlation-ID
Serilog technical logs
Activity logs queue
activity_logs table
Internal logs API/page
```

## 11. Testing Strategy

Unit tests:

```text
Validators
NRules
Cancellation policy
Idempotency hash/service
```

Integration tests:

```text
Concurrent stock deduction
Idempotent create race
Concurrent status update
Payment vs cancel race
Duplicate payment prevention
```

## 12. Known Limitations

```text
Payment provider is mocked.
No refresh token.
No distributed message broker.
Inventory service embedded in API for prototype.
Idempotency and order creation are not yet one shared transaction.
```

## 13. Future Improvements

```text
Shared UnitOfWork for idempotency + order transaction.
Outbox pattern.
OpenTelemetry metrics/tracing.
Rate limiting login/create order.
Refresh token.
Activity logs retention/partitioning.
External identity provider.
```