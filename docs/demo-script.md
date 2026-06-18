# Demo Script

## 1. Login

```bash
ADMIN_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"Password123!"}')

ADMIN_TOKEN=$(echo "$ADMIN_LOGIN" | jq -r '.accessToken')
```

```bash
CUSTOMER_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"customer1","password":"Password123!"}')

CUSTOMER_TOKEN=$(echo "$CUSTOMER_LOGIN" | jq -r '.accessToken')
CUSTOMER_ID=$(echo "$CUSTOMER_LOGIN" | jq -r '.user.id')
```

## 2. List Products

```bash
curl -k "https://localhost:7000/api/v1/products?page=1&pageSize=20" \
  -H "Authorization: Bearer $CUSTOMER_TOKEN"
```

## 3. Create Order

```bash
PRODUCT_ID="<product-id>"
IDEMPOTENCY_KEY=$(uuidgen)

ORDER_RESPONSE=$(curl -k -s -X POST https://localhost:7000/api/v1/orders \
  -H "Authorization: Bearer $CUSTOMER_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -H "X-Correlation-ID: demo-create-order-001" \
  -d "{
    \"customerId\": \"$CUSTOMER_ID\",
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
```

## 4. Retry Same Idempotency Key

```bash
curl -k -X POST https://localhost:7000/api/v1/orders \
  -H "Authorization: Bearer $CUSTOMER_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -d "{
    \"customerId\": \"$CUSTOMER_ID\",
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
Same stored response. No duplicate order. No duplicate stock deduction.
```

## 5. Payment Success

```bash
curl -k -X POST "https://localhost:7000/api/v1/orders/$ORDER_ID/payments" \
  -H "Authorization: Bearer $CUSTOMER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "provider": "MockPayment",
    "simulateResult": "Success"
  }' | jq
```

Expected:

```text
Payment Paid, Order Confirmed
```

## 6. Admin Update Status

Get row version:

```bash
ORDER_DETAIL=$(curl -k -s "https://localhost:7000/api/v1/orders/$ORDER_ID" \
  -H "Authorization: Bearer $ADMIN_TOKEN")

ROW_VERSION=$(echo "$ORDER_DETAIL" | jq -r '.rowVersion')
```

Ship:

```bash
curl -k -X PATCH "https://localhost:7000/api/v1/orders/$ORDER_ID/status" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"targetStatus\": \"Shipped\",
    \"expectedRowVersion\": $ROW_VERSION,
    \"reason\": \"Handed over to courier\"
  }" | jq
```

## 7. Cancel Because Stock Unavailable

For a Pending or Confirmed order:

```bash
curl -k -X POST "https://localhost:7000/api/v1/orders/$ORDER_ID/cancel" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"expectedRowVersion\": $ROW_VERSION,
    \"cancellationReason\": \"StockUnavailable\",
    \"reason\": \"Physical stock unavailable due to offline sale\"
  }" | jq
```

Expected:

```text
stockRestoreApplied = false
movement type = OrderCancelledNoRestore
```

## 8. Run Tests

```bash
dotnet test
```