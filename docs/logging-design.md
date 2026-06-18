# Logging & Activity Trace Design

## 1. Purpose

Logging design dibuat untuk membantu tracing production issue tanpa membebani request path dan tanpa membocorkan data sensitive.

Goals:

```text
- Trace request by correlation ID.
- Trace business activity by order ID/order number.
- Debug error with error code and event timeline.
- Keep request latency low.
- Avoid sensitive data leakage.
```

## 2. Correlation ID

Header:

```http
X-Correlation-ID
```

Behavior:

```text
If client sends it, API uses it.
If missing, API generates it.
Response always includes it.
Error response includes it.
Activity logs include it.
```

## 3. Technical Logs vs Activity Logs

### Technical Logs

Written through `ILogger` / Serilog.

Examples:

```text
Request started
Request completed
Unhandled exception
JWT authentication failed
Database migration applied
Background worker failed to insert batch
```

### Activity Logs

Written to `activity_logs` table through async queue.

Examples:

```text
OrderCreated
StockDeducted
PaymentPaid
OrderCancelled
StockNotRestored
IdempotencyReplayReturned
RequestFailed
```

## 4. Activity Log Queue Architecture

```text
Business flow / middleware
        |
        v
IActivityLogWriter
        |
        v
IActivityLogQueue
        |
        v
Bounded Channel
        |
        v
ActivityLogBackgroundWorker
        |
        v
ActivityLogRepository batch insert
        |
        v
PostgreSQL activity_logs
```

## 5. Why Queue?

Synchronous DB insert for every log can slow down API.

Queue design benefits:

```text
- Request thread only enqueues small message.
- Background worker performs batch insert.
- Bounded channel prevents unlimited memory growth.
- Failure to write non-critical logs does not fail business operation.
```

## 6. Activity Log Table

Table:

```text
activity_logs
```

Important columns:

```text
id
correlation_id
activity_type
actor_user_id
actor_username
actor_role
order_id
order_number
product_id
payment_id
request_path
http_method
status_code
elapsed_ms
error_code
before_state jsonb
after_state jsonb
metadata jsonb
created_at
```

Indexes:

```text
correlation_id
activity_type
actor_user_id
order_id
order_number
product_id
payment_id
created_at
error_code
```

## 7. Emitted Activity Events

Request:

```text
RequestCompleted
RequestFailed
```

Auth:

```text
LoginSucceeded
LoginFailed
```

Idempotency:

```text
IdempotencyAccepted
IdempotencyReplayReturned
IdempotencyConflict
```

Order:

```text
OrderCreateStarted
OrderCreated
OrderStatusChangeStarted
OrderStatusChanged
OrderStatusRejected
OrderCancelStarted
OrderCancelled
```

Inventory:

```text
StockDeducted
StockRestored
StockNotRestored
InsufficientStockDetected
```

Payment:

```text
PaymentCreateStarted
PaymentCreated
PaymentPaid
PaymentFailed
PaymentRejected
PaymentRefundRequired
```

Concurrency:

```text
ConcurrencyConflict
```

## 8. Sensitive Data Rules

Never log:

```text
Password
Password hash
JWT token
Authorization header
Full login body
Connection string
```

Allowed safe metadata:

```text
Order id
Order number
Product id
Payment id
Quantity
Stock before/after
Status before/after
Error code
Idempotency key prefix only
```

## 9. Internal Logs API

Admin/Ops only:

```http
GET /api/v1/internal/activity-logs
GET /api/v1/internal/activity-logs/{id}
```

Filters:

```text
correlationId
orderId
orderNumber
activityType
actorUserId
fromDate
toDate
page
pageSize
```

## 10. Internal Logs Page

Page:

```text
GET /internal/activity-logs
```

The HTML shell can be opened for demo, but data API still requires Admin/Ops JWT token.

Page capabilities:

```text
Search by correlation ID.
Search by order ID/order number.
View activity timeline.
Open detail with before/after/metadata JSON.
```

## 11. Performance Guidelines

```text
- Do not log full body by default.
- Keep metadata small.
- Use TryWrite for non-critical logs.
- Use EnqueueAsync only for critical logs if needed.
- Batch insert logs.
- Use indexed filters.
```

## 12. Failure Behavior

If activity log queue is full:

```text
TryWrite returns false.
Technical warning is logged.
Business request continues.
```

If background insert fails:

```text
Technical error log is written.
Worker delays briefly and continues.
```

## 13. What Must Not Happen

```text
- Password/JWT stored in activity_logs.
- Customer accesses internal logs API.
- Activity logs cause order/payment request failure.
- Queue is unbounded.
- Raw metadata rendered without HTML escaping.
```