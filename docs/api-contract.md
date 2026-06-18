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
    "role": "ApplicationAdmin"
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
Store operator JWT token includes storeId claim.
Store operators can only access data assigned to their store.
Store owner (SellerAdmin) can manage multiple stores.
```

---

## 3.2 Refresh Token

```http
POST /api/v1/auth/refresh-token
```

Access:

```text
Authenticated user (with valid refresh token)
```

Response `200 OK`:

```json
{
  "accessToken": "new-jwt-token",
  "expiresIn": 3600
}
```

Possible errors:

```text
401 UNAUTHORIZED
```

---

## 3.3 Logout

```http
POST /api/v1/auth/logout
```

Access:

```text
Authenticated user
```

Response `200 OK`

Possible errors:

```text
401 UNAUTHORIZED
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
storeId    optional UUID
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
      "description": "Wireless mouse with ergonomic design",
      "storeId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      "imageUrl": "https://cdn.example.com/images/mouse-001.jpg",
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
  "description": "Wireless mouse with ergonomic design",
  "storeId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "imageUrl": "https://cdn.example.com/images/mouse-001.jpg",
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
Buyer can create order only for themselves.
StoreBackofficeUser can create order for operational flow if allowed by service logic.
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
  "storeId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
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
storeId required
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
  "storeId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "storeName": "Toko Elektronik",
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
Buyer can only access own order
StoreBackofficeUser can access store orders
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
  "storeId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "storeName": "Toko Elektronik",
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
Buyer sees only own orders
StoreBackofficeUser can see store orders
```

Query parameters:

```text
status       optional string: Pending, Confirmed, Shipped, Delivered, Cancelled
customerId   optional UUID
storeId      optional UUID
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
      "storeId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      "storeName": "Toko Elektronik",
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
If Buyer sends customerId of another user, API still forces current buyer's own user id.
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
StoreBackofficeUser only
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
Buyer (order owner)
StoreBackofficeUser
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
Buyer can only use CustomerRequested reason.
StoreBackofficeUser can use all cancellation reasons.
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
Buyer (order owner)
StoreBackofficeUser
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
Buyer (order owner)
StoreBackofficeUser
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

# 7. Stores API

## 7.1 Open Store

```http
POST /api/v1/stores/open
```

Access:

```text
Buyer or SellerAdmin
```

Request:

```json
{
  "storeName": "Toko Elektronik",
  "description": "Menjual berbagai perlengkapan elektronik"
}
```

Response `201 Created`:

```json
{
  "id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "storeName": "Toko Elektronik",
  "description": "Menjual berbagai perlengkapan elektronik",
  "isActive": true,
  "createdAt": "2026-06-17T00:00:00Z"
}
```

Possible errors:

```text
400 VALIDATION_ERROR
401 UNAUTHORIZED
403 FORBIDDEN
409 STORE_NAME_ALREADY_EXISTS
```

---

## 7.2 Get My Stores

```http
GET /api/v1/stores/my
```

Access:

```text
Authenticated user
```

Response `200 OK`:

```json
{
  "stores": [
    {
      "id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      "storeName": "Toko Elektronik",
      "description": "Menjual berbagai perlengkapan elektronik",
      "isActive": true,
      "createdAt": "2026-06-17T00:00:00Z"
    }
  ]
}
```

Possible errors:

```text
401 UNAUTHORIZED
```

---

## 7.3 Get Store By Id

```http
GET /api/v1/stores/{storeId}
```

Access:

```text
Authenticated user
```

Route parameters:

```text
storeId required UUID
```

Response `200 OK`:

```json
{
  "id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "storeName": "Toko Elektronik",
  "description": "Menjual berbagai perlengkapan elektronik",
  "isActive": true,
  "createdAt": "2026-06-17T00:00:00Z"
}
```

Possible errors:

```text
401 UNAUTHORIZED
404 STORE_NOT_FOUND
```

---

## 7.4 Update Store

```http
PATCH /api/v1/stores/{storeId}
```

Access:

```text
Store backoffice user
```

Route parameters:

```text
storeId required UUID
```

Request:

```json
{
  "storeName": "Toko Elektronik Jaya",
  "description": "Menjual berbagai perlengkapan elektronik dan gadget"
}
```

Response `200 OK`:

```json
{
  "id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "storeName": "Toko Elektronik Jaya",
  "description": "Menjual berbagai perlengkapan elektronik dan gadget",
  "isActive": true,
  "updatedAt": "2026-06-17T00:00:00Z"
}
```

Possible errors:

```text
401 UNAUTHORIZED
403 FORBIDDEN
404 STORE_NOT_FOUND
409 CONCURRENT_UPDATE_CONFLICT
409 STORE_NAME_ALREADY_EXISTS
422 VALIDATION_ERROR
```

---

# 8. Store Operators API

Base path: `/api/v1/stores/{storeId}/operators`

Access: `StoreBackofficeUser`

## 8.1 List Operators

```http
GET /api/v1/stores/{storeId}/operators
```

Access:

```text
StoreBackofficeUser
```

Route parameters:

```text
storeId required UUID
```

Response `200 OK`:

```json
{
  "operators": [
    {
      "userId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
      "username": "operator1",
      "displayName": "Operator Satu",
      "isActive": true,
      "createdAt": "2026-06-17T00:00:00Z"
    }
  ]
}
```

Possible errors:

```text
401 UNAUTHORIZED
403 FORBIDDEN
404 STORE_NOT_FOUND
```

---

## 8.2 Create Operator

```http
POST /api/v1/stores/{storeId}/operators
```

Access:

```text
StoreBackofficeUser
```

Route parameters:

```text
storeId required UUID
```

Request:

```json
{
  "username": "operator1",
  "password": "Password123!",
  "displayName": "Operator Satu"
}
```

Response `201 Created`:

```json
{
  "userId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
  "username": "operator1",
  "displayName": "Operator Satu",
  "isActive": true,
  "createdAt": "2026-06-17T00:00:00Z"
}
```

Possible errors:

```text
401 UNAUTHORIZED
403 FORBIDDEN
404 STORE_NOT_FOUND
409 USERNAME_ALREADY_EXISTS
422 VALIDATION_ERROR
```

---

## 8.3 Update Operator Status

```http
PATCH /api/v1/stores/{storeId}/operators/{operatorUserId}/status
```

Access:

```text
StoreBackofficeUser
```

Route parameters:

```text
storeId        required UUID
operatorUserId required UUID
```

Request:

```json
{
  "isActive": false
}
```

Response `200 OK`:

```json
{
  "userId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
  "username": "operator1",
  "displayName": "Operator Satu",
  "isActive": false,
  "updatedAt": "2026-06-17T00:00:00Z"
}
```

Possible errors:

```text
401 UNAUTHORIZED
403 FORBIDDEN
404 STORE_NOT_FOUND
404 OPERATOR_NOT_FOUND
422 VALIDATION_ERROR
```

---

# 9. Backoffice Orders API

Base path: `/api/v1/backoffice/orders`

Access: `StoreBackofficeUser (SellerAdmin, SellerOperator, ApplicationAdmin)`

## 9.1 List Orders

```http
GET /api/v1/backoffice/orders
```

Access:

```text
StoreBackofficeUser
```

Query parameters:

```text
storeId      optional UUID
status       optional string: Pending, Confirmed, Shipped, Delivered, Cancelled
customerId   optional UUID
fromDate     optional DateTimeOffset
toDate       optional DateTimeOffset
page         optional int, default 1, min 1
pageSize     optional int, default 20, min 1, max 100
```

Response `200 OK`:

```json
{
  "items": [
    {
      "id": "72f3c9f0-78ff-4f2e-a462-7e9a6efb0001",
      "orderNumber": "ORD-20260617-000001",
      "storeId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      "storeName": "Toko Elektronik",
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

Possible errors:

```text
401 UNAUTHORIZED
403 FORBIDDEN
422 VALIDATION_ERROR
```

---

## 9.2 Get Order Detail

```http
GET /api/v1/backoffice/orders/{id}
```

Access:

```text
StoreBackofficeUser
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
  "storeId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "storeName": "Toko Elektronik",
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
```

---

## 9.3 Update Order Status

```http
PATCH /api/v1/backoffice/orders/{id}/status
```

Access:

```text
StoreBackofficeUser
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

Allowed transitions:

```text
Pending -> Confirmed
Confirmed -> Shipped
Shipped -> Delivered
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

---

## 9.4 Cancel Order

```http
POST /api/v1/backoffice/orders/{id}/cancel
```

Access:

```text
StoreBackofficeUser
```

Route parameters:

```text
id required UUID
```

Request:

```json
{
  "expectedRowVersion": 1,
  "cancellationReason": "OperationalIssue",
  "reason": "Stock unavailable from supplier."
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

Response `200 OK`:

```json
{
  "id": "72f3c9f0-78ff-4f2e-a462-7e9a6efb0001",
  "orderNumber": "ORD-20260617-000001",
  "previousStatus": "Pending",
  "currentStatus": "Cancelled",
  "cancellationReason": "OperationalIssue",
  "stockRestoreApplied": true,
  "rowVersion": 2,
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
422 ORDER_ALREADY_CANCELLED
422 ORDER_TERMINAL_STATE
422 INVALID_CANCELLATION_REASON
```

---

# 10. Backoffice Products API

Base path: `/api/v1/backoffice/products`

Access: `StoreBackofficeUser`

## 10.1 List Products

```http
GET /api/v1/backoffice/products
```

Access:

```text
StoreBackofficeUser
```

Query parameters:

```text
storeId    optional UUID
search     optional string, max length 100
isActive   optional boolean
page       optional int, default 1, min 1
pageSize   optional int, default 20, min 1, max 100
```

Response `200 OK`:

```json
{
  "items": [
    {
      "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "sku": "PRD-MOUSE-001",
      "name": "Mouse Wireless",
      "description": "Wireless mouse with ergonomic design",
      "storeId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      "imageUrl": "https://cdn.example.com/images/mouse-001.jpg",
      "stockQuantity": 15,
      "price": 150000,
      "rowVersion": 1,
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
403 FORBIDDEN
422 VALIDATION_ERROR
```

---

## 10.2 Get Product Detail

```http
GET /api/v1/backoffice/products/{id}
```

Access:

```text
StoreBackofficeUser
```

Route parameters:

```text
id required UUID
```

Response `200 OK`:

```json
{
  "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "sku": "PRD-MOUSE-001",
  "name": "Mouse Wireless",
  "description": "Wireless mouse with ergonomic design",
  "storeId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "imageUrl": "https://cdn.example.com/images/mouse-001.jpg",
  "stockQuantity": 15,
  "price": 150000,
  "rowVersion": 1,
  "isActive": true
}
```

Possible errors:

```text
401 UNAUTHORIZED
403 FORBIDDEN
404 PRODUCT_NOT_FOUND
```

---

## 10.3 Create Product

```http
POST /api/v1/backoffice/products
```

Access:

```text
StoreBackofficeUser
```

Request:

```json
{
  "storeId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "sku": "PRD-MOUSE-001",
  "name": "Mouse Wireless",
  "description": "Wireless mouse with ergonomic design",
  "stockQuantity": 50,
  "price": 150000
}
```

Response `201 Created`:

```json
{
  "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "sku": "PRD-MOUSE-001",
  "name": "Mouse Wireless",
  "description": "Wireless mouse with ergonomic design",
  "storeId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "stockQuantity": 50,
  "price": 150000,
  "rowVersion": 1,
  "isActive": true,
  "createdAt": "2026-06-17T00:00:00Z"
}
```

Possible errors:

```text
401 UNAUTHORIZED
403 FORBIDDEN
404 STORE_NOT_FOUND
409 SKU_ALREADY_EXISTS
422 VALIDATION_ERROR
```

---

## 10.4 Update Product

```http
PATCH /api/v1/backoffice/products/{id}
```

Access:

```text
StoreBackofficeUser
```

Route parameters:

```text
id required UUID
```

Request:

```json
{
  "name": "Mouse Wireless Pro",
  "description": "Updated description",
  "price": 175000,
  "expectedRowVersion": 1
}
```

Response `200 OK`:

```json
{
  "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "sku": "PRD-MOUSE-001",
  "name": "Mouse Wireless Pro",
  "description": "Updated description",
  "storeId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "stockQuantity": 50,
  "price": 175000,
  "rowVersion": 2,
  "isActive": true,
  "updatedAt": "2026-06-17T00:00:00Z"
}
```

Possible errors:

```text
401 UNAUTHORIZED
403 FORBIDDEN
404 PRODUCT_NOT_FOUND
409 CONCURRENT_UPDATE_CONFLICT
422 VALIDATION_ERROR
```

---

## 10.5 Update Product Status

```http
PATCH /api/v1/backoffice/products/{id}/status
```

Access:

```text
StoreBackofficeUser
```

Route parameters:

```text
id required UUID
```

Request:

```json
{
  "isActive": false,
  "expectedRowVersion": 2
}
```

Response `200 OK`:

```json
{
  "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "sku": "PRD-MOUSE-001",
  "name": "Mouse Wireless",
  "isActive": false,
  "rowVersion": 3,
  "updatedAt": "2026-06-17T00:00:00Z"
}
```

Possible errors:

```text
401 UNAUTHORIZED
403 FORBIDDEN
404 PRODUCT_NOT_FOUND
409 CONCURRENT_UPDATE_CONFLICT
422 VALIDATION_ERROR
```

---

## 10.6 Adjust Stock

```http
POST /api/v1/backoffice/products/{id}/stock/adjust
```

Access:

```text
StoreBackofficeUser
```

Route parameters:

```text
id required UUID
```

Request:

```json
{
  "adjustmentType": "Increase",
  "quantity": 10,
  "expectedRowVersion": 2,
  "reason": "Restock from supplier"
}
```

Adjustment types:

```text
Increase
Decrease
Set
```

Response `200 OK`:

```json
{
  "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "sku": "PRD-MOUSE-001",
  "name": "Mouse Wireless",
  "previousStockQuantity": 50,
  "currentStockQuantity": 60,
  "adjustmentType": "Increase",
  "adjustmentQuantity": 10,
  "rowVersion": 3,
  "updatedAt": "2026-06-17T00:00:00Z"
}
```

Possible errors:

```text
401 UNAUTHORIZED
403 FORBIDDEN
404 PRODUCT_NOT_FOUND
409 CONCURRENT_UPDATE_CONFLICT
422 VALIDATION_ERROR
422 INSUFFICIENT_STOCK
```

---

## 10.7 Upload Image

```http
POST /api/v1/backoffice/products/{id}/image
```

Access:

```text
StoreBackofficeUser
```

Route parameters:

```text
id required UUID
```

Request:

```text
multipart/form-data, max 5MB
```

Response `200 OK`:

```json
{
  "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "imageUrl": "https://cdn.example.com/images/product-001.jpg",
  "updatedAt": "2026-06-17T00:00:00Z"
}
```

Possible errors:

```text
400 FILE_TOO_LARGE
400 INVALID_FILE_TYPE
401 UNAUTHORIZED
403 FORBIDDEN
404 PRODUCT_NOT_FOUND
422 VALIDATION_ERROR
```

---

# 11. Backoffice Dashboard API

```http
GET /api/v1/backoffice/dashboard
```

Access:

```text
StoreBackofficeUser
```

Query parameters:

```text
storeId           optional UUID
lowStockThreshold optional int, default 10
```

Response `200 OK`:

```json
{
  "storeId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "storeName": "Toko Elektronik",
  "totalProducts": 150,
  "activeProducts": 120,
  "inactiveProducts": 30,
  "lowStockProducts": 5,
  "pendingOrders": 12,
  "confirmedOrders": 8,
  "shippedOrders": 15,
  "cancelledOrders": 3,
  "todayOrders": 7,
  "todayRevenue": 2500000,
  "generatedAt": "2026-06-17T10:00:00Z"
}
```

Possible errors:

```text
401 UNAUTHORIZED
403 FORBIDDEN
404 STORE_NOT_FOUND
```

---

# 12. Internal Activity Logs API

Activity logs API digunakan untuk tracing operational.

## 12.1 List Activity Logs

```http
GET /api/v1/internal/activity-logs
```

Access:

```text
ApplicationAdmin, DevOps only
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
      "actorRole": "Buyer",
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
Buyer cannot access internal activity logs.
Anonymous cannot access internal activity logs.
```

---

## 12.2 Get Activity Log Detail

```http
GET /api/v1/internal/activity-logs/{id}
```

Access:

```text
ApplicationAdmin, DevOps only
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
  "actorRole": "Buyer",
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

## 12.3 Internal Activity Logs Page

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
Data API still requires ApplicationAdmin/DevOps JWT token.
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
Buyer token must receive 403 when calling internal activity logs API.
Page must not store token in localStorage/sessionStorage.
Page escapes rendered dynamic values.
```

---

# 13. Demo API

```http
POST /api/v1/demo/concurrent-stock-deduction
```

Access:

```text
Authenticated user
```

Request:

```json
{
  "productId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "quantity": 5,
  "customerId": "33333333-3333-3333-3333-333333333333"
}
```

Response `200 OK`:

```json
{
  "message": "Concurrent stock deduction demo completed",
  "productId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "requestedQuantity": 5,
  "deductedQuantity": 5,
  "remainingStock": 10
}
```

Possible errors:

```text
400 VALIDATION_ERROR
401 UNAUTHORIZED
404 PRODUCT_NOT_FOUND
409 INSUFFICIENT_STOCK
```

---

# 14. Diagnostics API

Access:

```text
ApplicationAdmin, DevOps only
```

## 14.1 Health OK

```http
GET /api/v1/diagnostics/ok
```

Response `200 OK`:

```json
{
  "status": "OK"
}
```

## 14.2 Business Rule Exception

```http
GET /api/v1/diagnostics/app-error
```

Disabled in production.

Response `422 Unprocessable Entity`:

```json
{
  "error": {
    "code": "DEMO_BUSINESS_ERROR",
    "message": "This is a demo business rule exception.",
    "details": [],
    "correlationId": "diag-001",
    "timestamp": "2026-06-17T00:00:00Z"
  }
}
```

## 14.3 Unhandled Exception

```http
GET /api/v1/diagnostics/unhandled-error
```

Disabled in production.

Response `500 Internal Server Error`:

```json
{
  "error": {
    "code": "INTERNAL_SERVER_ERROR",
    "message": "An unexpected error occurred.",
    "details": [],
    "correlationId": "diag-002",
    "timestamp": "2026-06-17T00:00:00Z"
  }
}
```

---

# 15. Health Check

## 15.1 Health

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

# 16. Swagger

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

# 17. Activity Log Event Types

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
StockAdjusted

PaymentCreateStarted
PaymentCreated
PaymentPaid
PaymentFailed
PaymentRejected
PaymentRefundRequired

StoreCreated
StoreUpdated
StoreOperatorCreated
StoreOperatorStatusChanged

BackofficeProductCreated
BackofficeProductUpdated
BackofficeProductStatusChanged
ProductImageUploaded

ConcurrencyConflict
```

---

# 18. Authorization Summary

Roles:

```text
Buyer
SellerAdmin
SellerOperator
ApplicationAdmin
DevOps
```

Store Access:

```text
StoreBackofficeUser = SellerAdmin, SellerOperator, ApplicationAdmin
Store owner sees all store data
Store operator sees assigned store data
```

Customer data isolation:

```text
Buyer sees own orders only
SellerAdmin sees own store orders
```

Access rules:

```text
Auth login:
  Anonymous

Products (public):
  Authenticated user

Create order:
  Buyer for self
  StoreBackofficeUser operational flow

Get/list orders:
  Buyer own orders only
  StoreBackofficeUser store orders

Update status:
  StoreBackofficeUser only

Cancel:
  Buyer (order owner)
  StoreBackofficeUser

Payments:
  Buyer (order owner)
  StoreBackofficeUser

Stores:
  Open: Buyer or SellerAdmin
  My stores: Authenticated user
  Get by id: Authenticated user
  Update: Store backoffice user

Store operators:
  StoreBackofficeUser

Backoffice orders:
  StoreBackofficeUser

Backoffice products:
  StoreBackofficeUser

Backoffice dashboard:
  StoreBackofficeUser

Internal activity logs:
  ApplicationAdmin, DevOps only

Demo:
  Authenticated user

Diagnostics:
  ApplicationAdmin, DevOps only
```

---

# 19. Important Business Rules

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

# 20. Security Notes

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
