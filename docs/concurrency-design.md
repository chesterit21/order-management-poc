# Concurrency Design

## 1. Purpose

Concurrency design di project ini dibuat untuk memastikan sistem tetap benar saat banyak request masuk bersamaan. Fokus utama:

```text
- Stock tidak boleh minus.
- Double order harus dicegah.
- Status order tidak boleh inconsistent.
- Cancel tidak boleh restore stock dua kali.
- Payment dan cancel tidak boleh menghasilkan state yang bertabrakan.
```

Concurrency handling dilakukan terutama dengan:

```text
- PostgreSQL transaction
- SELECT ... FOR UPDATE
- deterministic lock ordering
- row_version check
- database unique constraint
- database check constraint
- NRules validation after lock
```

## 2. Transaction Isolation

Default isolation yang dipakai:

```text
READ COMMITTED + explicit row-level locking
```

Alasan:

```text
READ COMMITTED cukup jika semua critical resource dikunci eksplisit.
SERIALIZABLE lebih mahal dan dapat meningkatkan retry transaction.
Untuk POC ini, resource kritis jelas: products dan orders.
```

## 3. Concurrent Stock Deduction

### Problem

```text
Product X stock = 15
User A order qty 10
User B order qty 10
Keduanya masuk bersamaan
```

Tanpa locking, dua request bisa sama-sama membaca stock 15 dan keduanya deduct, menyebabkan stock minus atau oversell.

### Implementation

Order creation menjalankan transaction dan mengunci product rows:

```sql
SELECT
    id,
    stock_quantity,
    price
FROM products
WHERE id = ANY(@ProductIds)
ORDER BY id
FOR UPDATE;
```

Setelah lock:

```text
1. Product rows terkunci sampai commit/rollback.
2. API membaca stock terbaru.
3. API validasi stock cukup.
4. API deduct stock.
5. API insert inventory movement.
6. API commit.
```

Semua critical transactions juga menjalankan:

```sql
SET LOCAL lock_timeout = '5s';
```

Ini memastikan request tidak hang terlalu lama saat menunggu lock dan gagal cepat dengan conflict/error yang jelas.

### Deadlock Prevention

Semua product dikunci dengan urutan yang sama:

```sql
ORDER BY id
```

Ini mencegah deadlock klasik:

```text
Request A lock Product 1 lalu Product 2
Request B lock Product 2 lalu Product 1
```

### Expected Result

```text
One request succeeds.
One request fails with INSUFFICIENT_STOCK.
Final stock = 5.
Stock never negative.
```

## 3.1 Demo Endpoint

Tersedia endpoint demo untuk menguji concurrent stock deduction secara sederhana:

```http
POST /api/v1/demo/concurrent-stock-deduction
```

### Implementation

`DemoController` mengirim **2 request order creation secara paralel** untuk product yang sama menggunakan `Task.WhenAll`:

```text
Request A -> POST /api/v1/orders (create order for product)
Request B -> POST /api/v1/orders (create order for product)
        \             /
         FOR UPDATE lock on products row
              |
     One succeeds, one fails
```

### Idempotency

DemoController **men-generate Idempotency-Key unik secara internal per request**, sehingga `RequireIdempotencyKeyFilter` (yang mewajibkan header) di-bypass. Ini memungkinkan demo mengirim 2 request independen tanpa konflik idempotency key.

### Expected Result

```text
Satu order berhasil dibuat (stock ter-deduct).
Satu order gagal dengan INSUFFICIENT_STOCK.
Final stock = 5 (assuming initial 15, qty 10 each).
Stock never negative.
```

## 4. Database Constraint as Last Defense

Table `products` punya constraint:

```sql
CHECK (stock_quantity >= 0)
```

Ini adalah last line of defense jika ada bug di application logic.

Table `products` juga memiliki kolom `store_id` yang menghubungkan produk ke store tertentu.

## 5. Idempotent Create Under Race

### Problem

Dua request create order dengan `Idempotency-Key` sama datang bersamaan sebelum salah satu commit.

### Protection

Table `idempotency_keys` punya unique constraint:

```sql
UNIQUE (user_id, key, endpoint)
```

Flow:

```text
1. Request mencoba insert idempotency record InProgress.
2. Hanya satu request yang menang.
3. Request yang menang lanjut create order.
4. Request lain membaca existing record.
5. Jika InProgress, return 409 REQUEST_ALREADY_IN_PROGRESS.
6. Jika Completed, return stored response.
```

### Expected Result

```text
Only one order created.
Only one stock deduction.
No duplicate order.
```

## 6. Concurrent Status Update

### Problem

```text
Order status = Confirmed
ApplicationAdmin A update to Shipped
ApplicationAdmin B cancel order
Same time
```

### Protection

Update status dan cancel sama-sama lock order row:

```sql
SELECT
    id,
    status,
    row_version
FROM orders
WHERE id = @OrderId
FOR UPDATE;
```

Setelah lock:

```text
1. API membaca latest status.
2. API membandingkan expectedRowVersion.
3. API menjalankan NRules berdasarkan latest status.
4. API update status dan increment row_version.
5. API insert status history.
```

### Expected Result

```text
Only one operation succeeds.
Loser receives 409 CONCURRENT_UPDATE_CONFLICT or 422 INVALID_ORDER_STATUS_TRANSITION.
Final status valid.
```

## 7. Double Cancel Race

### Problem

Dua cancel request untuk order yang sama dapat menyebabkan stock restore dua kali.

### Protection

```text
- Order row locked FOR UPDATE.
- row_version checked.
- cancel only valid for Pending/Confirmed.
- stock restore only executed after valid transition.
```

### Expected Result

```text
First cancel succeeds.
Second cancel fails.
Stock restored at most once.
```

## 8. Cancel Stock Restore vs No Restore

Cancel reason menentukan stock behavior:

```text
CustomerRequested   -> restore stock
OperationalIssue    -> restore stock
FraudSuspected      -> restore stock
StockUnavailable    -> do not restore stock
InventoryMismatch   -> do not restore stock
```

### Why No Restore?

Jika ApplicationAdmin cancel karena stock fisik habis akibat offline/manual sale, system stock tidak boleh ditambah lagi.

Example:

```text
System stock awal = 10
Online order deduct 10 -> system stock = 0
Warehouse check: physical stock already sold offline
ApplicationAdmin cancel reason StockUnavailable
System stock must remain 0
```

Jika stock direstore menjadi 10, system akan overstate physical stock.

### Audit

Cancel restore:

```text
inventory_movements.movement_type = OrderCancelledRestore
```

Cancel no restore:

```text
inventory_movements.movement_type = OrderCancelledNoRestore
```

## 9. Payment vs Cancel Race

Payment dan cancel sama-sama lock order row.

### Payment Wins

```text
1. Payment locks Pending order.
2. Payment success inserts Paid payment.
3. Order becomes Confirmed.
4. Cancel waits.
5. Cancel reads Confirmed.
6. Cancel still allowed.
7. Paid payment becomes RefundRequired.
```

### Cancel Wins

```text
1. Cancel locks Pending order.
2. Order becomes Cancelled.
3. Payment waits.
4. Payment reads Cancelled.
5. NRules rejects payment.
```

### Expected Result

```text
No Paid payment on Cancelled order without refund marker.
No payment success for terminal order.
```

## 10. Duplicate Payment Prevention

Layers:

```text
1. Order row lock FOR UPDATE.
2. Check existing Paid payment.
3. NRules payment validation.
4. Partial unique index on payments(order_id) WHERE status = 'Paid'.
```

Expected:

```text
Only one successful Paid payment per order.
```

## 11. Lock Timeout

Critical transaction sets:

```sql
SET LOCAL lock_timeout = '5s';
```

Purpose:

```text
Avoid request hanging too long when waiting for locks.
Fail fast and return clear conflict/error.
```

## 12. Race Conditions Covered

```text
- Concurrent stock deduction.
- Concurrent status update.
- Idempotent create race.
- Double cancel.
- Payment vs cancel.
- Duplicate payment.
- Offline/manual stock mismatch cancel.
```

## 13. Must Not Happen

```text
- Stock negative.
- Two orders with same idempotency key.
- Two Paid payments for one order.
- Double stock restore.
- Buyer seeing another buyer's order.
- PATCH status setting Cancelled.
- Terminal state changed again.
```
