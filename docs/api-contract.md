# API Contract - Order Management API

Dokumen ini menjelaskan kontrak API untuk Order Management API POC.

Base URL:

```text
/api/v1
```

Local development example:

```text
https://localhost:7000
```

---

## 1. Common Headers

### Authorization

Sebagian besar endpoint membutuhkan JWT Bearer token.

```http
Authorization: Bearer {accessToken}
```

### Correlation ID

Optional. Jika tidak dikirim, API akan generate otomatis.

```http
X-Correlation-ID: client-correlation-id
```

Response akan selalu mengembalikan header:

```http
X-Correlation-ID: same-correlation-id
```

### Idempotency Key

Wajib untuk create order.

```http
Idempotency-Key: unique-key-per-submit
```

Digunakan hanya untuk:

```http
POST /api/v1/orders
```

---

## 2. Common Error Response

Semua error menggunakan format standar:

```json
{
  "error": {
    "code": "ERROR_CODE",
    "message": "Human readable message.",
    "details": [
      {
        "field": "fieldName",
        "message": "Field error message.",
        "metadata": {}
      }
    ],
    "correlationId": "trace-id",
    "timestamp": "2026-06-17T00:00:00Z"
  }
}
```

Common HTTP status:

```text
400 Bad Request
401 Unauthorized
403 Forbidden
404 Not Found
409 Conflict
422 Unprocessable Entity
500 Internal Server Error
```

---

# 3. Auth API

## 3.1 Login

```http
POST /api/v1/auth/login
```

Access:

```text
Anonymous
```

Request:

```json
{
  "username": "admin",
  "password": "Password123!"
}
```

Response `200 OK`:

```json
{
  "accessToken": "jwt-token",
  "expiresIn": 3600,
  "user": {
    "id": "11111111-1111-1111-1111-111111111111",
    "username": "admin",
    "displayName": "System Admin",
    "role": "Admin"
  }
}
```

Possible errors:

```text
401 INVALID_CREDENTIALS
422 VALIDATION_ERROR
```

Example invalid credential response:

```json
{
  "error": {
    "code": "INVALID_CREDENTIALS",
    "message": "Invalid username or password.",
    "details": [],
    "correlationId": "login-001",
    "timestamp": "2026-06-17T00:00:00Z"
  }
}
```

Security notes:

```text
Password is never returned.
Password hash is never returned.
JWT token is not stored in activity logs.
Invalid username and invalid password return the same generic error.
```

---

# 4. Products API

## 4.1 List Products

```http
GET /api/v1/products
```

Access:

```text
Authenticated user
```

Query parameters:

```text
search     optional string, max length 100
page       optional int, default 1, min 1
pageSize   optional int, default 20, min 1, max 100
```

Example:

```http
GET /api/v1/products?search=mouse&page=1&pageSize=20
```

Response `200 OK`:

```json
{
  "items": [
    {
      "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "sku": "PRD-MOUSE-001",
      "name": "Mouse Wireless",
      "stockQuantity": 15,
      "price": 150000,
      "isActive": true
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 20,
    "totalItems": 1,
    "totalPages": 1
  }
}
```

Possible errors:

```text
401 UNAUTHORIZED
422 VALIDATION_ERROR
```

---

## 4.2 Get Product Detail

```http
GET /api/v1/products/{id}
```

Access:

```text
Authenticated user
```

Route parameters:

```text
id  required UUID
```

Response `200 OK`:

```json
{
  "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "sku": "PRD-MOUSE-001",
  "name": "Mouse Wireless",
  "stockQuantity": 15,
  "price": 150000,
  "rowVersion": 1,
  "isActive": true
}
```

Possible errors:

```text
401 UNAUTHORIZED
404 PRODUCT_NOT_FOUND
422 VALIDATION_ERROR
```

---

# 5. Orders API

## 5.1 Create Order

```http
POST /api/v1/orders
```

Access:

```text
Authenticated user
Customer can create order only for themselves.
Admin/Ops can create order for operational flow if allowed by service logic.
```

Required headers:

```http
Authorization: Bearer {accessToken}
Idempotency-Key: {unique-key}
```

Request:

```json
{
  "customerId": "33333333-3333-3333-3333-333333333333",
  "items": [
    {
      "productId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "quantity": 10
    }
  ],
  "shippingAddress": "Jl. Example No. 1, Tangerang Selatan"
}
```

Validation rules:

```text
customerId required
items required, at least one item
duplicate productId not allowed
productId required
quantity > 0
shippingAddress required
Idempotency-Key required, max length 200
```

Response `201 Created`:

```json
{
  "id": "72f3c9f0-78ff-4f2e-a462-7e9a6efb0001",
  "orderNumber": "ORD-20260617-000001",
  "customerId": "33333333-3333-3333-3333-333333333333",
  "status": "Pending",
  "shippingAddress": "Jl. Example No. 1, Tangerang Selatan",
  "totalAmount": 1500000,
  "rowVersion": 1,
  "items": [
    {
      "productId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "productName": "Mouse Wireless",
      "quantity": 10,
      "unitPrice": 150000,
      "lineTotal": 1500000
    }
  ],
  "createdAt": "2026-06-17T00:00:00Z"
}
```

Idempotency behavior:

```text
New key + new payload:
  Process request and create order.

Same key + same payload + completed:
  Return stored response.

Same key + same payload + in progress:
  409 REQUEST_ALREADY_IN_PROGRESS.

Same key + different payload:
  409 IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD.
```

Possible errors:

```text
400 IDEMPOTENCY_KEY_REQUIRED
401 UNAUTHORIZED
403 FORBIDDEN
404 PRODUCT_NOT_FOUND
409 INSUFFICIENT_STOCK
409 REQUEST_ALREADY_IN_PROGRESS
409 IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD
422 VALIDATION_ERROR
500 INTERNAL_SERVER_ERROR
```

Example insufficient stock error:

```json
{
  "error": {
    "code": "INSUFFICIENT_STOCK",
    "message": "Stock has changed. Product Mouse Wireless currently has only 5 units available.",
    "details": [
      {
        "field": "items.quantity",
        "message": "Requested quantity exceeds available stock.",
        "metadata": {
          "productId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
          "requestedQuantity": 10,
          "availableQuantity": 5
        }
      }
    ],
    "correlationId": "create-order-001",
    "timestamp": "2026-06-17T00:00:00Z"
  }
}
```

---

## 5.2 Get Order Detail

```http
GET /api/v1/orders/{id}
```

Access:

```text
Authenticated user
Customer can only access own order
Admin/Ops can access all orders
```

Route parameters:

```text
id required UUID
```

Response `200 OK`:

```json
{
  "id": "72f3c9f0-78ff-4f2e-a462-7e9a6efb0001",
  "orderNumber": "ORD-20260617-000001",
  "customerId": "33333333-3333-3333-3333-333333333333",
  "customerName": "Customer One",
  "status": "Pending",
  "shippingAddress": "Jl. Example No. 1, Tangerang Selatan",
  "totalAmount": 1500000,
  "rowVersion": 1,
  "items": [
    {
      "productId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "productName": "Mouse Wireless",
      "quantity": 10,
      "unitPrice": 150000,
      "lineTotal": 1500000
    }
  ],
  "statusHistory": [
    {
      "fromStatus": null,
      "toStatus": "Pending",
      "reason": "Order created.",
      "changedBy": "33333333-3333-3333-3333-333333333333",
      "changedAt": "2026-06-17T00:00:00Z"
    }
  ],
  "createdAt": "2026-06-17T00:00:00Z",
  "updatedAt": "2026-06-17T00:00:00Z"
}
```

Possible errors:

```text
401 UNAUTHORIZED
403 FORBIDDEN
404 ORDER_NOT_FOUND
422 VALIDATION_ERROR
```

---

## 5.3 List Orders

```http
GET /api/v1/orders
```

Access:

```text
Authenticated user
Customer sees only own orders
Admin/Ops can see all orders
```

Query parameters:

```text
status       optional string: Pending, Confirmed, Shipped, Delivered, Cancelled
customerId   optional UUID
fromDate     optional DateTimeOffset
toDate       optional DateTimeOffset
page         optional int, default 1, min 1
pageSize     optional int, default 20, min 1, max 100
```

Example:

```http
GET /api/v1/orders?status=Pending&page=1&pageSize=20
```

Response `200 OK`:

```json
{
  "items": [
    {
      "id": "72f3c9f0-78ff-4f2e-a462-7e9a6efb0001",
      "orderNumber": "ORD-20260617-000001",
      "customerId": "33333333-3333-3333-3333-333333333333",
      "customerName": "Customer One",
      "status": "Pending",
      "totalAmount": 1500000,
      "rowVersion": 1,
      "createdAt": "2026-06-17T00:00:00Z",
      "updatedAt": "2026-06-17T00:00:00Z"
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 20,
    "totalItems": 1,
    "totalPages": 1
  }
}
```

Security behavior:

```text
If customer sends customerId of another user, API still forces current customer's own user id.
```

Possible errors:

```text
401 UNAUTHORIZED
403 FORBIDDEN
422 VALIDATION_ERROR
```

---

## 5.4 Update Order Status

```http
PATCH /api/v1/orders/{id}/status
```

Access:

```text
Admin/Ops only
```

Route parameters:

```text
id required UUID
```

Request:

```json
{
  "targetStatus": "Shipped",
  "expectedRowVersion": 2,
  "reason": "Handed over to courier."
}
```

Validation rules:

```text
targetStatus required
targetStatus must be valid OrderStatus
targetStatus cannot be Cancelled
expectedRowVersion > 0
reason max length 500
```

Allowed transitions:

```text
Pending -> Confirmed
Confirmed -> Shipped
Shipped -> Delivered
```

Important:

```text
Cancelled status cannot be set through this endpoint.
Use POST /api/v1/orders/{id}/cancel instead.
```

Response `200 OK`:

```json
{
  "id": "72f3c9f0-78ff-4f2e-a462-7e9a6efb0001",
  "orderNumber": "ORD-20260617-000001",
  "previousStatus": "Confirmed",
  "currentStatus": "Shipped",
  "rowVersion": 3,
  "updatedAt": "2026-06-17T00:00:00Z"
}
```

Possible errors:

```text
401 UNAUTHORIZED
403 FORBIDDEN
404 ORDER_NOT_FOUND
409 CONCURRENT_UPDATE_CONFLICT
422 VALIDATION_ERROR
422 INVALID_ORDER_STATUS_TRANSITION
422 ORDER_TERMINAL_STATE
422 CANCELLED_STATUS_REQUIRES_CANCEL_ENDPOINT
```

Example row version conflict:

```json
{
  "error": {
    "code": "CONCURRENT_UPDATE_CONFLICT",
    "message": "Order has been modified by another user. Please refresh and try again.",
    "details": [
      {
        "field": "expectedRowVersion",
        "message": "Expected row version does not match current row version.",
        "metadata": {
          "expected": 2,
          "current": 3
        }
      }
    ],
    "correlationId": "update-status-001",
    "timestamp": "2026-06-17T00:00:00Z"
  }
}
```

---

## 5.5 Cancel Order

```http
POST /api/v1/orders/{id}/cancel
```

Access:

```text
Customer owner
Admin
Ops
```

Route parameters:

```text
id required UUID
```

Request:

```json
{
  "expectedRowVersion": 1,
  "cancellationReason": "CustomerRequested",
  "reason": "Customer changed mind."
}
```

Cancellation reasons:

```text
CustomerRequested
StockUnavailable
InventoryMismatch
OperationalIssue
FraudSuspected
```

Cancellation stock policy:

```text
CustomerRequested   -> restore stock
OperationalIssue    -> restore stock
FraudSuspected      -> restore stock
StockUnavailable    -> do not restore stock
InventoryMismatch   -> do not restore stock
```

Customer rule:

```text
Customer can only use CustomerRequested reason.
Admin/Ops can use all cancellation reasons.
```

Response `200 OK` with stock restore:

```json
{
  "id": "72f3c9f0-78ff-4f2e-a462-7e9a6efb0001",
  "orderNumber": "ORD-20260617-000001",
  "previousStatus": "Pending",
  "currentStatus": "Cancelled",
  "cancellationReason": "CustomerRequested",
  "stockRestoreApplied": true,
  "rowVersion": 2,
  "stockRestored": [
    {
      "productId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "quantity": 10
    }
  ],
  "stockNotRestored": [],
  "paymentRefundRequired": false,
  "updatedAt": "2026-06-17T00:00:00Z"
}
```

Response `200 OK` without stock restore:

```json
{
  "id": "72f3c9f0-78ff-4f2e-a462-7e9a6efb0001",
  "orderNumber": "ORD-20260617-000001",
  "previousStatus": "Pending",
  "currentStatus": "Cancelled",
  "cancellationReason": "StockUnavailable",
  "stockRestoreApplied": false,
  "rowVersion": 2,
  "stockRestored": [],
  "stockNotRestored": [
    {
      "productId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "quantity": 10,
      "reason": "StockUnavailable"
    }
  ],
  "paymentRefundRequired": false,
  "updatedAt": "2026-06-17T00:00:00Z"
}
```

If order has paid payment:

```text
paymentRefundRequired = true
payment status is updated to RefundRequired
```

Possible errors:

```text
401 UNAUTHORIZED
403 FORBIDDEN
404 ORDER_NOT_FOUND
409 CONCURRENT_UPDATE_CONFLICT
422 VALIDATION_ERROR
422 INVALID_ORDER_STATUS_TRANSITION
422 ORDER_ALREADY_CANCELLED
422 ORDER_TERMINAL_STATE
422 INVALID_CANCELLATION_REASON
```

---

# 6. Payments API

## 6.1 Create Payment

```http
POST /api/v1/orders/{orderId}/payments
```

Access:

```text
Customer owner
Admin
Ops
```

Route parameters:

```text
orderId required UUID
```

Request:

```json
{
  "provider": "MockPayment",
  "simulateResult": "Success"
}
```

Simulation result:

```text
Success
Failed
```

Payment rules:

```text
Payment only allowed when order status is Pending.
Payment success changes order status Pending -> Confirmed.
Payment failed keeps order status Pending.
Duplicate Paid payment is prevented.
```

Response `200 OK` for success:

```json
{
  "paymentId": "7ce85e22-1c58-42f0-a987-f83be4770001",
  "orderId": "72f3c9f0-78ff-4f2e-a462-7e9a6efb0001",
  "amount": 1500000,
  "status": "Paid",
  "orderStatus": "Confirmed",
  "provider": "MockPayment",
  "paymentReference": "MOCK-20260617-ABC123",
  "createdAt": "2026-06-17T00:00:00Z"
}
```

Response `200 OK` for failed payment:

```json
{
  "paymentId": "7ce85e22-1c58-42f0-a987-f83be4770002",
  "orderId": "72f3c9f0-78ff-4f2e-a462-7e9a6efb0001",
  "amount": 1500000,
  "status": "Failed",
  "orderStatus": "Pending",
  "provider": "MockPayment",
  "paymentReference": "MOCK-20260617-DEF456",
  "createdAt": "2026-06-17T00:00:00Z"
}
```

Possible errors:

```text
401 UNAUTHORIZED
403 FORBIDDEN
404 ORDER_NOT_FOUND
422 VALIDATION_ERROR
422 PAYMENT_NOT_ALLOWED
422 PAYMENT_ALREADY_PAID
409 CONCURRENT_UPDATE_CONFLICT
```

---

## 6.2 List Payments by Order

```http
GET /api/v1/orders/{orderId}/payments
```

Access:

```text
Customer owner
Admin
Ops
```

Route parameters:

```text
orderId required UUID
```

Response `200 OK`:

```json
{
  "orderId": "72f3c9f0-78ff-4f2e-a462-7e9a6efb0001",
  "payments": [
    {
      "id": "7ce85e22-1c58-42f0-a987-f83be4770001",
      "amount": 1500000,
      "status": "Paid",
      "provider": "MockPayment",
      "paymentReference": "MOCK-20260617-ABC123",
      "createdAt": "2026-06-17T00:00:00Z",
      "updatedAt": "2026-06-17T00:00:00Z"
    }
  ]
}
```

Possible errors:

```text
401 UNAUTHORIZED
403 FORBIDDEN
404 ORDER_NOT_FOUND
```

---

# 7. Internal Activity Logs API

Activity logs API digunakan untuk tracing operational.

## 7.1 List Activity Logs

```http
GET /api/v1/internal/activity-logs
```

Access:

```text
Admin/Ops only
```

Query parameters:

```text
correlationId optional string, max 100
orderId       optional UUID
orderNumber   optional string, max 50
activityType  optional string, max 100
actorUserId   optional UUID
fromDate      optional DateTimeOffset
toDate        optional DateTimeOffset
page          optional int, default 1, min 1
pageSize      optional int, default 50, min 1, max 200
```

Example:

```http
GET /api/v1/internal/activity-logs?correlationId=demo-create-order-001&page=1&pageSize=50
```

Response `200 OK`:

```json
{
  "items": [
    {
      "id": "7a18f65e-531c-48ef-9273-7c7908cf0001",
      "correlationId": "demo-create-order-001",
      "activityType": "OrderCreated",
      "actorUserId": "33333333-3333-3333-3333-333333333333",
      "actorUsername": "customer1",
      "actorRole": "Customer",
      "orderId": "72f3c9f0-78ff-4f2e-a462-7e9a6efb0001",
      "orderNumber": "ORD-20260617-000001",
      "productId": null,
      "paymentId": null,
      "requestPath": "/api/v1/orders",
      "httpMethod": "POST",
      "statusCode": null,
      "elapsedMs": null,
      "errorCode": null,
      "metadataJson": "{\"customerId\":\"33333333-3333-3333-3333-333333333333\",\"itemCount\":1}",
      "createdAt": "2026-06-17T00:00:00Z"
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 50,
    "totalItems": 1,
    "totalPages": 1
  }
}
```

Possible errors:

```text
401 UNAUTHORIZED
403 FORBIDDEN
422 VALIDATION_ERROR
```

Security behavior:

```text
Customer cannot access internal activity logs.
Anonymous cannot access internal activity logs.
```

---

## 7.2 Get Activity Log Detail

```http
GET /api/v1/internal/activity-logs/{id}
```

Access:

```text
Admin/Ops only
```

Route parameters:

```text
id required UUID
```

Response `200 OK`:

```json
{
  "id": "7a18f65e-531c-48ef-9273-7c7908cf0001",
  "correlationId": "demo-create-order-001",
  "activityType": "OrderCreated",
  "actorUserId": "33333333-3333-3333-3333-333333333333",
  "actorUsername": "customer1",
  "actorRole": "Customer",
  "orderId": "72f3c9f0-78ff-4f2e-a462-7e9a6efb0001",
  "orderNumber": "ORD-20260617-000001",
  "productId": null,
  "paymentId": null,
  "requestPath": "/api/v1/orders",
  "httpMethod": "POST",
  "statusCode": null,
  "elapsedMs": null,
  "errorCode": null,
  "beforeStateJson": null,
  "afterStateJson": "{\"status\":\"Pending\",\"totalAmount\":1500000}",
  "metadataJson": "{\"customerId\":\"33333333-3333-3333-3333-333333333333\",\"itemCount\":1}",
  "createdAt": "2026-06-17T00:00:00Z"
}
```

Possible errors:

```text
401 UNAUTHORIZED
403 FORBIDDEN
404 ACTIVITY_LOG_NOT_FOUND
422 VALIDATION_ERROR
```

---

## 7.3 Internal Activity Logs Page

```http
GET /internal/activity-logs
```

Purpose:

```text
Simple internal HTML page for tracing activity log timeline.
```

Access behavior:

```text
HTML shell can be opened for demo.
Data API still requires Admin/Ops JWT token.
```

Page supports filters:

```text
correlationId
orderId
orderNumber
activityType
actorUserId
fromDate
toDate
```

Security:

```text
Customer token must receive 403 when calling internal activity logs API.
Page must not store token in localStorage/sessionStorage.
Page escapes rendered dynamic values.
```

---

# 8. Health Check

## 8.1 Health

```http
GET /health
```

Access:

```text
Anonymous
```

Response `200 OK`:

```json
{
  "status": "Healthy"
}
```

Actual response shape may depend on ASP.NET Core health check default output if not customized.

---

# 9. Swagger

Swagger endpoint:

```http
GET /swagger
```

Swagger JSON:

```http
GET /swagger/v1/swagger.json
```

Swagger supports:

```text
JWT Bearer authorization
X-Correlation-ID header documentation
Idempotency-Key header documentation for POST /api/v1/orders
```

---

# 10. Activity Log Event Types

The system may emit these activity log types:

```text
RequestCompleted
RequestFailed

LoginSucceeded
LoginFailed

IdempotencyAccepted
IdempotencyReplayReturned
IdempotencyConflict

OrderCreateStarted
OrderCreated

OrderStatusChangeStarted
OrderStatusChanged
OrderStatusRejected

OrderCancelStarted
OrderCancelled

StockDeducted
StockRestored
StockNotRestored
InsufficientStockDetected

PaymentCreateStarted
PaymentCreated
PaymentPaid
PaymentFailed
PaymentRejected
PaymentRefundRequired

ConcurrencyConflict
```

---

# 11. Authorization Summary

Roles:

```text
Customer
Admin
Ops
```

Access rules:

```text
Auth login:
  Anonymous

Products:
  Authenticated user

Create order:
  Customer for self
  Admin/Ops operational flow

Get/list orders:
  Customer own orders only
  Admin/Ops all orders

Update status:
  Admin/Ops only

Cancel:
  Customer owner
  Admin/Ops

Payments:
  Customer owner
  Admin/Ops

Internal activity logs:
  Admin/Ops only
```

---

# 12. Important Business Rules

## Order Status Transition

Allowed:

```text
Pending -> Confirmed
Pending -> Cancelled via cancel endpoint only

Confirmed -> Shipped
Confirmed -> Cancelled via cancel endpoint only

Shipped -> Delivered

Delivered terminal
Cancelled terminal
```

## Cancel Endpoint Rule

```text
Do not use PATCH /status to cancel order.
Always use POST /cancel.
```

Reason:

```text
Cancel must handle stock restore/no-restore, payment refund marker, inventory movement, and status history.
```

## Payment Rule

```text
Payment is only allowed when order status is Pending.
Payment success changes order to Confirmed.
```

## Duplicate Payment Rule

```text
Only one Paid payment is allowed per order.
```

Protected by:

```text
Order row lock
NRules validation
Existing paid payment check
Partial unique index
```

---

# 13. Security Notes

The API must never return or log:

```text
password
password hash
JWT token
Authorization header
connection string
raw SQL with sensitive parameter
stack trace in client response
```

Activity logs should only include safe metadata:

```text
correlationId
orderId
orderNumber
productId
paymentId
quantity
stock before/after
status before/after
error code
idempotency key prefix
```