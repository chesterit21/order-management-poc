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
13 controllers:
  AuthController
  ProductsController
  OrdersController
  PaymentsController
  StoresController
  StoreOperatorsController
  BackofficeOrdersController
  BackofficeProductsController
  BackofficeDashboardController
  ActivityLogsController
  DemoController
  DiagnosticsController
  InternalActivityLogsTestController

HTTP contracts
Middleware (order: CorrelationId -> RequestLogging -> GlobalExceptionHandling -> Auth -> Controllers)
Filters: RequireIdempotencyKeyFilter
Swagger setup
Authentication/authorization setup
CORS
```

### OrderManagement.Application

```text
Use case services:
  AuthService
  OrderService
  BackofficeOrderService
  ProductService
  ProductManagementService
  PaymentService
  StoreService
  StoreOperatorService
  StoreAuthorizationService
  BackofficeDashboardService
  ActivityLogService
  DemoService
  OrderCancellationPolicy

DTOs/Commands/Results
20 validators:
  LoginCommandValidator
  CreateOrderCommandValidator
  CancelOrderCommandValidator
  UpdateOrderStatusCommandValidator
  ListOrdersQueryValidator
  BackofficeCancelOrderCommandValidator
  BackofficeOrderListQueryDtoValidator
  BackofficeUpdateOrderStatusCommandValidator
  CreatePaymentCommandValidator
  ProductListQueryDtoValidator
  BackofficeProductListQueryDtoValidator
  CreateProductCommandValidator
  UpdateProductCommandValidator
  SetProductStatusCommandValidator
  AdjustProductStockCommandValidator
  OpenStoreCommandValidator
  UpdateStoreCommandValidator
  CreateStoreOperatorCommandValidator
  SetStoreOperatorStatusCommandValidator
  BackofficeDashboardSummaryQueryDtoValidator

Application exceptions:
  AppException (base)
  BusinessRuleAppException
  ConcurrencyAppException
  ConflictAppException
  ForbiddenAppException
  IdempotencyConflictException
  NotFoundAppException
  UnauthorizedAppException
  ValidationAppException

Interfaces/abstractions
Business orchestration
Authorization decisions based on current user
Cancellation policy
```

### OrderManagement.Domain

```text
Entities (9 + base classes):
  Entity (base: Id, CreatedAt, UpdatedAt)
  AuditableEntity (extends Entity: CreatedBy, UpdatedBy)
  Order
  OrderItem
  OrderStatusHistory
  Product
  User
  Store
  StoreMember
  Payment
  IdempotencyRecord
  InventoryMovement

Enums:
  OrderStatus
  PaymentStatus
  UserRole (Buyer, SellerAdmin, SellerOperator, ApplicationAdmin, DevOps)
  OrderCancellationReason
  InventoryMovementType
  StockAdjustmentType
  StoreMemberRole (Owner, Operator)
  IdempotencyStatus

Value objects:
  Money (struct)
  OrderNumber (record)
  Sku (record)

Rule facts:
  OrderTransitionFact
  CancelOrderFact
  PaymentFact

Rule result:
  RuleValidationResult

Domain constants
```

### OrderManagement.Infrastructure

```text
Dapper repositories:
  OrderRepository
  ProductRepository
  UserRepository
  StoreRepository
  PaymentRepository
  IdempotencyRepository
  ActivityLogRepository
  BackofficeOrderRepository
  ProductManagementRepository
  BackofficeDashboardRepository

PostgreSQL connection factory
Migration runner
JWT generator
BCrypt password hasher
Current user context
NRules implementation (8 rules):
  PendingToConfirmedRule
  ConfirmedToShippedRule
  ShippedToDeliveredRule
  PendingToCancelledRule
  ConfirmedToCancelledRule
  CancelAllowedRule
  PaymentAllowedRule
  TerminalOrderStateRule

Activity logs:
  ActivityLogQueue (dual priority Channel)
  ActivityLogWriter
  ActivityLogBackgroundWorker
  ActivityLogRepository

Idempotency persistence
Request hashing (SHA-256 normalized JSON)

File storage:
  LocalProductImageStorageService (saves to wwwroot/uploads/products)

Options classes:
  DatabaseOptions
  MigrationOptions
  JwtOptions
  IdempotencyOptions
  ActivityLogOptions
  FileUploadOptions
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
store_id
description
primary_image_url
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

## 5.6 Stores

Endpoints:

```text
POST /api/v1/stores/open — Open store (Buyer/SellerAdmin)
GET /api/v1/stores/my — My stores
GET /api/v1/stores/{storeId} — Store detail
PATCH /api/v1/stores/{storeId} — Update store
```

Store Operators:

```text
GET /api/v1/stores/{storeId}/operators — List operators
POST /api/v1/stores/{storeId}/operators — Create operator
PATCH /api/v1/stores/{storeId}/operators/{userId}/status — Set status
```

## 5.7 Backoffice

Orders:

```text
GET /api/v1/backoffice/orders — List orders (store-scoped)
GET /api/v1/backoffice/orders/{id} — Order detail
PATCH /api/v1/backoffice/orders/{id}/status — Update status
POST /api/v1/backoffice/orders/{id}/cancel — Cancel
```

Products:

```text
GET /api/v1/backoffice/products — List/store-scoped
GET /api/v1/backoffice/products/{id} — Detail
POST /api/v1/backoffice/products — Create
PATCH /api/v1/backoffice/products/{id} — Update
PATCH /api/v1/backoffice/products/{id}/status — Set active/inactive
POST /api/v1/backoffice/products/{id}/stock/adjust — Stock adjustment
POST /api/v1/backoffice/products/{id}/image — Upload image (max 5MB)
```

Dashboard:

```text
GET /api/v1/backoffice/dashboard — Summary stats
```

## 5.8 Demo

```text
POST /api/v1/demo/concurrent-stock-deduction — Demo concurrent stock scenario
```

## 5.9 Diagnostics

ApplicationAdmin/DevOps only:

```text
GET /api/v1/diagnostics/ok — Health check
GET /api/v1/diagnostics/app-error — Test business rule exception (non-prod only)
GET /api/v1/diagnostics/unhandled-error — Test unhandled exception (non-prod only)
```

## 5.10 Idempotency

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

## 5.11 Activity Logs

Activity logs are emitted through async queue and persisted by background worker.

Internal tracing:

```text
GET /api/v1/internal/activity-logs
GET /api/v1/internal/activity-logs/{id}
GET /internal/activity-logs
```

ApplicationAdmin/DevOps only for data API.

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
stores
store_members
```

Key column details:

```text
products: store_id, description, primary_image_url, CHECK (stock_quantity >= 0)
orders: store_id
order_items: price, subtotal, nullable product_name_snapshot
payments: created_by
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

Currently 19 migration files (001 through 019):

```text
001: create_extensions
002: create_users
003: create_products
004: create_orders
005: create_order_items
006: create_inventory_movements
007: create_order_status_history
008: create_idempotency_keys
009: create_payments
010: create_indexes
011: update_inventory_movement_types
012: create_order_number_sequence
013: create_activity_logs
014: create_stores_and_update_user_roles
015: update_products_for_store_ownership
016: add_store_id_to_orders
017: add_price_to_order_items
018: add_subtotal_to_order_items
019: allow_null_product_name_snapshot
```

## 8. Seed Files

Currently 4 seed files:

```text
001_seed_users.sql — 5 users with roles Buyer, SellerAdmin, SellerOperator, ApplicationAdmin, DevOps
002_seed_products.sql — 3 products
003_seed_stores.sql — Creates Seller One Store
004_assign_products_to_seed_store.sql — Assigns products to store
```

## 9. Critical Transactions

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
Update order Cancelled + row_version
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

## 10. Security Design

```text
JWT bearer authentication
Role-based authorization
BCrypt password hashing
No token/password logging
Consistent error response without stack trace
Internal logs API ApplicationAdmin/DevOps only
Customer data isolation

Store-scoped data isolation:
- StoreOwner sees own store data
- StoreOperator sees assigned store data
- Users can only act within their store context
```

## 11. Observability

```text
X-Correlation-ID
Serilog technical logs
Activity logs queue
activity_logs table
Internal logs API/page
```

## 12. Testing Strategy

Unit tests:

```text
Validators for 15+ commands
NRules for 8 rules (PendingToConfirmed, ConfirmedToShipped, etc.)
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
API endpoint integration tests (auth, orders, payments, products)
```

## 13. Known Limitations

```text
Payment provider is mocked.
No refresh token.
No distributed message broker.
Inventory service embedded in API for prototype.
Idempotency and order creation are not yet one shared transaction.
```

## 14. Future Improvements

```text
Shared UnitOfWork for idempotency + order transaction.
Outbox pattern.
OpenTelemetry metrics/tracing.
Rate limiting login/create order.
Refresh token.
Activity logs retention/partitioning.
External identity provider.
```
