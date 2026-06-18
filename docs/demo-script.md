# Demo Script

## 1. Login as Different Roles

```bash
# ── ApplicationAdmin (full access) ──
ADMIN_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"appadmin","password":"Password123!"}')
ADMIN_TOKEN=$(echo "$ADMIN_LOGIN" | jq -r '.accessToken')
echo "AppAdmin:$(echo "$ADMIN_LOGIN" | jq -r '.user.role')"

# ── SellerAdmin (store management) ──
SELLER_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"selleradmin1","password":"Password123!"}')
SELLER_TOKEN=$(echo "$SELLER_LOGIN" | jq -r '.accessToken')
SELLER_ID=$(echo "$SELLER_LOGIN" | jq -r '.user.id')
echo "SellerAdmin:$(echo "$SELLER_LOGIN" | jq -r '.user.role') id=$SELLER_ID"

# ── Buyer1 (buyer flow) ──
BUYER_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"buyer1","password":"Password123!"}')
BUYER_TOKEN=$(echo "$BUYER_LOGIN" | jq -r '.accessToken')
BUYER_ID=$(echo "$BUYER_LOGIN" | jq -r '.user.id')
echo "Buyer:$(echo "$BUYER_LOGIN" | jq -r '.user.role') id=$BUYER_ID"
```

## 2. Open a Store (SellerAdmin)

```bash
# SellerAdmin opens their store
OPEN_STORE=$(curl -k -s -X POST https://localhost:7000/api/v1/stores/open \
  -H "Authorization: Bearer $SELLER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "storeName": "My Demo Store",
    "description": "A demo store for the walkthrough."
  }')
echo "$OPEN_STORE" | jq
STORE_ID=$(echo "$OPEN_STORE" | jq -r '.id')

# Get my stores
curl -k -s "https://localhost:7000/api/v1/stores/my" \
  -H "Authorization: Bearer $SELLER_TOKEN" | jq

# Update store
curl -k -s -X PATCH "https://localhost:7000/api/v1/stores/$STORE_ID" \
  -H "Authorization: Bearer $SELLER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "storeName": "My Demo Store (Updated)",
    "description": "Updated description."
  }' | jq

# Create an operator for the store
curl -k -s -X POST "https://localhost:7000/api/v1/stores/$STORE_ID/operators" \
  -H "Authorization: Bearer $SELLER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "username": "operator1",
    "password": "Password123!",
    "displayName": "Store Operator One"
  }' | jq
```

## 3. List / View Products

```bash
# Public product listing (no auth required)
curl -k -s "https://localhost:7000/api/v1/products?page=1&pageSize=20" | jq

# Pick a product for the order flow
PRODUCT_ID=$(curl -k -s "https://localhost:7000/api/v1/products?page=1&pageSize=20" \
  | jq -r '.items[0].id')
PRODUCT_STOCK=$(curl -k -s "https://localhost:7000/api/v1/products/$PRODUCT_ID" \
  | jq -r '.stockQuantity')
echo "Product: $PRODUCT_ID (stock=$PRODUCT_STOCK)"
```

## 4. Create Order (Buyer)

```bash
IDEMPOTENCY_KEY=$(uuidgen)

ORDER_RESPONSE=$(curl -k -s -X POST https://localhost:7000/api/v1/orders \
  -H "Authorization: Bearer $BUYER_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -H "X-Correlation-ID: demo-create-order-001" \
  -d "{
    \"customerId\": \"$BUYER_ID\",
    \"items\": [
      {
        \"productId\": \"$PRODUCT_ID\",
        \"quantity\": 1
      }
    ],
    \"shippingAddress\": \"Jl. Demo No. 1\"
  }")

echo "$ORDER_RESPONSE" | jq
ORDER_ID=$(echo "$ORDER_RESPONSE" | jq -r '.id')
ORDER_VERSION=$(echo "$ORDER_RESPONSE" | jq -r '.rowVersion')
echo "Order created: $ORDER_ID (rowVersion=$ORDER_VERSION)"
```

Expected:

```text
201 Created — order is Pending, stock deducted atomically.
```

## 5. Test Idempotency Replay

```bash
# Replay the exact same request with the same Idempotency-Key
curl -k -s -X POST https://localhost:7000/api/v1/orders \
  -H "Authorization: Bearer $BUYER_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -d "{
    \"customerId\": \"$BUYER_ID\",
    \"items\": [
      {
        \"productId\": \"$PRODUCT_ID\",
        \"quantity\": 1
      }
    ],
    \"shippingAddress\": \"Jl. Demo No. 1\"
  }" | jq
```

Expected:

```text
Same stored response returned (200/201). No duplicate order. No duplicate stock deduction.
```

## 6. Process Payment

```bash
curl -k -s -X POST "https://localhost:7000/api/v1/orders/$ORDER_ID/payments" \
  -H "Authorization: Bearer $BUYER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "provider": "MockPayment",
    "simulateResult": "Success"
  }' | jq
```

Expected:

```text
Payment Paid, Order transitions from Pending → Confirmed
```

## 7. Update Status Lifecycle (Pending → Confirmed → Shipped → Delivered)

```bash
# Get fresh row version after payment
ORDER_DETAIL=$(curl -k -s "https://localhost:7000/api/v1/orders/$ORDER_ID" \
  -H "Authorization: Bearer $BUYER_TOKEN")
ROW_VERSION=$(echo "$ORDER_DETAIL" | jq -r '.rowVersion')

# Buyer confirms
curl -k -s -X PATCH "https://localhost:7000/api/v1/orders/$ORDER_ID/status" \
  -H "Authorization: Bearer $BUYER_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"targetStatus\": \"Confirmed\",
    \"expectedRowVersion\": $ROW_VERSION,
    \"reason\": \"Buyer confirmed the order\"
  }" | jq

# Refresh version and ship (appadmin only — buyers cannot ship)
ORDER_DETAIL=$(curl -k -s "https://localhost:7000/api/v1/orders/$ORDER_ID" \
  -H "Authorization: Bearer $ADMIN_TOKEN")
ROW_VERSION=$(echo "$ORDER_DETAIL" | jq -r '.rowVersion')

curl -k -s -X PATCH "https://localhost:7000/api/v1/orders/$ORDER_ID/status" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"targetStatus\": \"Shipped\",
    \"expectedRowVersion\": $ROW_VERSION,
    \"reason\": \"Handed over to courier\"
  }" | jq

# Refresh version and deliver
ORDER_DETAIL=$(curl -k -s "https://localhost:7000/api/v1/orders/$ORDER_ID" \
  -H "Authorization: Bearer $ADMIN_TOKEN")
ROW_VERSION=$(echo "$ORDER_DETAIL" | jq -r '.rowVersion')

curl -k -s -X PATCH "https://localhost:7000/api/v1/orders/$ORDER_ID/status" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"targetStatus\": \"Delivered\",
    \"expectedRowVersion\": $ROW_VERSION,
    \"reason\": \"Delivered to customer address\"
  }" | jq
```

## 8. Cancel Order (with Stock Restore Policy)

Create a fresh order for cancellation demo:

```bash
CANCEL_KEY=$(uuidgen)

CANCEL_ORDER_RESP=$(curl -k -s -X POST https://localhost:7000/api/v1/orders \
  -H "Authorization: Bearer $BUYER_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: $CANCEL_KEY" \
  -d "{
    \"customerId\": \"$BUYER_ID\",
    \"items\": [
      {
        \"productId\": \"$PRODUCT_ID\",
        \"quantity\": 1
      }
    ],
    \"shippingAddress\": \"Jl. Cancel Demo\"
  }")
CANCEL_ORDER_ID=$(echo "$CANCEL_ORDER_RESP" | jq -r '.id')
CANCEL_VERSION=$(echo "$CANCEL_ORDER_RESP" | jq -r '.rowVersion')

# Cancel with StockUnavailable reason (no restore)
curl -k -s -X POST "https://localhost:7000/api/v1/orders/$CANCEL_ORDER_ID/cancel" \
  -H "Authorization: Bearer $BUYER_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"expectedRowVersion\": $CANCEL_VERSION,
    \"cancellationReason\": \"StockUnavailable\",
    \"reason\": \"Physical stock unavailable due to offline sale\"
  }" | jq
```

Expected:

```text
stockRestoreApplied = false
movement type = OrderCancelledNoRestore
```

Cancel with normal reason (stock restored):

```bash
# Create yet another order
RESTORE_KEY=$(uuidgen)
RESTORE_ORDER_RESP=$(curl -k -s -X POST https://localhost:7000/api/v1/orders \
  -H "Authorization: Bearer $BUYER_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: $RESTORE_KEY" \
  -d "{
    \"customerId\": \"$BUYER_ID\",
    \"items\": [
      {
        \"productId\": \"$PRODUCT_ID\",
        \"quantity\": 1
      }
    ],
    \"shippingAddress\": \"Jl. Restore Demo\"
  }")
RESTORE_ORDER_ID=$(echo "$RESTORE_ORDER_RESP" | jq -r '.id')
RESTORE_VERSION=$(echo "$RESTORE_ORDER_RESP" | jq -r '.rowVersion')

curl -k -s -X POST "https://localhost:7000/api/v1/orders/$RESTORE_ORDER_ID/cancel" \
  -H "Authorization: Bearer $BUYER_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"expectedRowVersion\": $RESTORE_VERSION,
    \"cancellationReason\": \"CustomerRequest\",
    \"reason\": \"Buyer changed mind\"
  }" | jq
```

Expected:

```text
stockRestoreApplied = true
stock restored to product inventory
```

## 9. Backoffice: List Orders / Update Status

```bash
# List backoffice orders (selleradmin1 sees their store's orders)
curl -k -s "https://localhost:7000/api/v1/backoffice/orders?page=1&pageSize=20" \
  -H "Authorization: Bearer $SELLER_TOKEN" | jq

# Get order detail
BO_ORDER_DETAIL=$(curl -k -s "https://localhost:7000/api/v1/backoffice/orders/$ORDER_ID" \
  -H "Authorization: Bearer $SELLER_TOKEN")
BO_VERSION=$(echo "$BO_ORDER_DETAIL" | jq -r '.rowVersion')

# Backoffice status update
curl -k -s -X PATCH "https://localhost:7000/api/v1/backoffice/orders/$ORDER_ID/status" \
  -H "Authorization: Bearer $SELLER_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"targetStatus\": \"Shipped\",
    \"expectedRowVersion\": $BO_VERSION,
    \"reason\": \"Shipped by backoffice\"
  }" | jq

# Backoffice cancel
BO_VERSION=$(curl -k -s "https://localhost:7000/api/v1/backoffice/orders/$CANCEL_ORDER_ID" \
  -H "Authorization: Bearer $SELLER_TOKEN" | jq -r '.rowVersion')

curl -k -s -X POST "https://localhost:7000/api/v1/backoffice/orders/$CANCEL_ORDER_ID/cancel" \
  -H "Authorization: Bearer $SELLER_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"expectedRowVersion\": $BO_VERSION,
    \"cancellationReason\": \"StockUnavailable\",
    \"reason\": \"Cancelled via backoffice\"
  }" | jq
```

## 10. Backoffice: Manage Products

```bash
# ── List products ──
curl -k -s "https://localhost:7000/api/v1/backoffice/products?page=1&pageSize=20" \
  -H "Authorization: Bearer $SELLER_TOKEN" | jq

# ── Create a new product ──
NEW_PRODUCT=$(curl -k -s -X POST https://localhost:7000/api/v1/backoffice/products \
  -H "Authorization: Bearer $SELLER_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"storeId\": \"$STORE_ID\",
    \"sku\": \"PRD-DEMO-001\",
    \"name\": \"Demo Product\",
    \"description\": \"Created during demo walkthrough.\",
    \"stockQuantity\": 100,
    \"price\": 25000
  }")
echo "$NEW_PRODUCT" | jq
NEW_PROD_ID=$(echo "$NEW_PRODUCT" | jq -r '.id')
NEW_PROD_VERSION=$(echo "$NEW_PRODUCT" | jq -r '.rowVersion')

# ── Update product ──
curl -k -s -X PATCH "https://localhost:7000/api/v1/backoffice/products/$NEW_PROD_ID" \
  -H "Authorization: Bearer $SELLER_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"name\": \"Demo Product (Updated)\",
    \"description\": \"Updated description.\",
    \"price\": 30000,
    \"expectedRowVersion\": $NEW_PROD_VERSION
  }" | jq

# ── Set product status (deactivate) ──
NEW_PROD_VERSION=$(curl -k -s "https://localhost:7000/api/v1/backoffice/products/$NEW_PROD_ID" \
  -H "Authorization: Bearer $SELLER_TOKEN" | jq -r '.rowVersion')

curl -k -s -X PATCH "https://localhost:7000/api/v1/backoffice/products/$NEW_PROD_ID/status" \
  -H "Authorization: Bearer $SELLER_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"isActive\": false,
    \"expectedRowVersion\": $NEW_PROD_VERSION
  }" | jq

# ── Adjust stock ──
NEW_PROD_VERSION=$(curl -k -s "https://localhost:7000/api/v1/backoffice/products/$NEW_PROD_ID" \
  -H "Authorization: Bearer $SELLER_TOKEN" | jq -r '.rowVersion')

curl -k -s -X POST "https://localhost:7000/api/v1/backoffice/products/$NEW_PROD_ID/stock/adjust" \
  -H "Authorization: Bearer $SELLER_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"adjustmentType\": \"Increase\",
    \"quantity\": 50,
    \"expectedRowVersion\": $NEW_PROD_VERSION,
    \"reason\": \"Restock from supplier\"
  }" | jq

# ── Upload product image ──
# Create a small test image
echo "fake-image-data" > /tmp/test-product-image.png

curl -k -s -X POST "https://localhost:7000/api/v1/backoffice/products/$NEW_PROD_ID/image" \
  -H "Authorization: Bearer $SELLER_TOKEN" \
  -F "file=@/tmp/test-product-image.png" | jq
```

## 11. Backoffice Dashboard

```bash
# Dashboard summary (all stores)
curl -k -s "https://localhost:7000/api/v1/backoffice/dashboard" \
  -H "Authorization: Bearer $SELLER_TOKEN" | jq

# Dashboard summary filtered by store
curl -k -s "https://localhost:7000/api/v1/backoffice/dashboard?storeId=$STORE_ID&lowStockThreshold=10" \
  -H "Authorization: Bearer $SELLER_TOKEN" | jq
```

Expected:

```text
{
  "storeId": "...",
  "storeName": "...",
  "totalProducts": ...,
  "activeProducts": ...,
  "inactiveProducts": ...,
  "lowStockProducts": ...,
  "pendingOrders": ...,
  "confirmedOrders": ...,
  "shippedOrders": ...,
  "cancelledOrders": ...,
  "todayOrders": ...,
  "todayRevenue": ...,
  "generatedAt": "..."
}
```

## 12. Activity Logs (Internal — AppAdmin / DevOps only)

```bash
# List activity logs (newest first)
curl -k -s "https://localhost:7000/api/v1/internal/activity-logs?page=1&pageSize=10" \
  -H "Authorization: Bearer $ADMIN_TOKEN" | jq

# Filter by activity type
curl -k -s "https://localhost:7000/api/v1/internal/activity-logs?activityType=OrderCreated&pageSize=5" \
  -H "Authorization: Bearer $ADMIN_TOKEN" | jq

# Filter by specific correlation ID
curl -k -s "https://localhost:7000/api/v1/internal/activity-logs?correlationId=demo-create-order-001" \
  -H "Authorization: Bearer $ADMIN_TOKEN" | jq
```

## 13. Diagnostics Endpoints (AppAdmin / DevOps only)

```bash
# OK health check
curl -k -s "https://localhost:7000/api/v1/diagnostics/ok" \
  -H "Authorization: Bearer $ADMIN_TOKEN" | jq

# Application error (business rule exception — returns 409)
curl -k -s "https://localhost:7000/api/v1/diagnostics/app-error" \
  -H "Authorization: Bearer $ADMIN_TOKEN" | jq

# Unhandled error (500 with ProblemDetails)
curl -k -s "https://localhost:7000/api/v1/diagnostics/unhandled-error" \
  -H "Authorization: Bearer $ADMIN_TOKEN" | jq
```

## 14. Demo Concurrent Stock Deduction

```bash
# Pick a product with limited stock
DEMO_PROD_ID=$(curl -k -s "https://localhost:7000/api/v1/products?page=1&pageSize=20" \
  | jq -r '.items[0].id')
DEMO_STOCK=$(curl -k -s "https://localhost:7000/api/v1/products/$DEMO_PROD_ID" \
  | jq -r '.stockQuantity')
echo "Product $DEMO_PROD_ID has stock=$DEMO_STOCK"

# Fire concurrent stock deduction demo
# The endpoint spawns 2 concurrent orders for the same product.
# With FOR UPDATE locks, only one succeeds when stock is insufficient.
curl -k -s -X POST https://localhost:7000/api/v1/demo/concurrent-stock-deduction \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"productId\": \"$DEMO_PROD_ID\",
    \"quantity\": 9999,
    \"customerId\": \"$BUYER_ID\",
    \"shippingAddress\": \"Concurrent Demo Address\"
  }" | jq
```

Expected:

```text
{
  "scenario": "Two concurrent orders for 9999 units each...",
  "initialStock": <N>,
  "quantityEach": 9999,
  "requests": [
    { "statusCode": 201, "orderId": "...", "orderNumber": "..." },
    { "statusCode": 409, "errorCode": "INSUFFICIENT_STOCK", ... }
  ],
  "finalStock": <N>,
  "summary": "1 succeeded, 1 rejected — stock never goes below 0."
}
```
