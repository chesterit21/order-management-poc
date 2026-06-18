# Order Management API POC

Prototype REST API untuk sistem manajemen order yang dirancang untuk menangani masalah-masalah yang sering terjadi di sistem distribusi: *double order* akibat *double-click*, *stock minus* akibat *concurrent request*, status order tidak konsisten akibat *multiple admin update*, dan minimnya *logging* untuk *tracing*.

## Latar Belakang

Klien adalah perusahaan distribusi yang mengalami beberapa masalah pada sistem *order management* mereka:

1. **Double Order** — *User* tidak sabar menunggu, mengklik tombol "Submit" berulang kali sehingga tercipta banyak order yang sama.
2. **Stock Minus** — Dua *request* masuk bersamaan di milidetik yang sama untuk produk yang sama, sistem tidak *handle* dengan benar sehingga *stock* menjadi negatif.
3. **Status Order Tidak Konsisten** — Saat *multiple admin* meng-*update* order yang sama secara bersamaan, status *update*-nya tidak konsisten.
4. **Minim Logging** — Tim *ops* kesulitan melakukan *tracing error* karena kurangnya *logging*.

## Ringkasan Solusi

| Masalah | Solusi |
|---|---|
| Double order | Idempotency-Key + unique constraint `(user_id, key, endpoint)` |
| Stock minus | `SELECT ... FOR UPDATE` + `CHECK (stock_quantity >= 0)` |
| Status inkonsisten | Row-level locking + `row_version` + NRules validasi |
| Minim logging | Correlation ID + Serilog + activity log async queue |

---

## Daftar Isi

- [Tech Stack](#tech-stack)
- [Struktur Solusi](#struktur-solusi)
- [Layer Arsitektur](#layer-arsitektur)
- [Fitur Utama](#fitur-utama)
- [Siklus Hidup Order](#siklus-hidup-order)
- [Idempotency](#idempotency)
- [Concurrency Handling](#concurrency-handling)
- [Alasan Memilih PostgreSQL](#alasan-memilih-postgresql)
- [Error Handling](#error-handling)
- [Logging & Tracing](#logging--tracing)
- [Database Migration](#database-migration)
- [Race Conditions yang Dicover](#race-conditions-yang-dicover)
- [Endpoint API](#endpoint-api)
- [Role & Authorization](#role--authorization)
- [Run Lokal](#run-lokal)
- [Setup Database](#setup-database)
- [Run Tests](#run-tests)
- [Daftar Dokumen](#daftar-dokumen)
- [Keterbatasan](#keterbatasan)

---

## Tech Stack

| Teknologi | Kegunaan |
|---|---|
| ASP.NET Core Web API (.NET 10) | Framework API |
| PostgreSQL | Database utama |
| Dapper | ORM ringan |
| NRules | *Business rule engine* untuk validasi transisi status |
| JWT Bearer Authentication | Autentikasi |
| BCrypt.Net-Next | *Password hashing* |
| Serilog | *Structured logging* |
| xUnit + Testcontainers PostgreSQL | *Unit test* & *integration test* |

---

## Struktur Solusi

```
src/
  OrderManagement.Api          # Controllers, middleware, Swagger, auth setup
  OrderManagement.Application  # Use case services, validators, DTOs, abstractions
  OrderManagement.Domain       # Entities, enums, rule facts/results
  OrderManagement.Infrastructure # Dapper repositories, NRules, idempotency, migration runner

tests/
  OrderManagement.Tests              # Unit test
  OrderManagement.IntegrationTests   # Integration test dengan Testcontainers

db/
  migrations/  # SQL migration files
  seed/        # Seed data (users, products)

docs/          # Dokumentasi lengkap
postman/       # Postman collection
scripts/       # Convenience scripts (run-api.sh, reset-db.sh)
```

---

## Layer Arsitektur

### OrderManagement.Api — *HTTP Concerns*

- Controllers (`AuthController`, `ProductsController`, `OrdersController`, `PaymentsController`, `InternalActivityLogsController`)
- Middleware: `CorrelationIdMiddleware`, `RequestLoggingMiddleware`, `GlobalExceptionHandlingMiddleware`
- Swagger setup
- Authentication / Authorization setup
- Internal Activity Logs Page (HTML)

### OrderManagement.Application — *Use Case Orchestration*

- Application services (`AuthService`, `OrderService`, `PaymentService`, `BackofficeOrderService`, dll)
- FluentValidation validators
- Application exceptions hierarchy
- DTOs / Commands / Results
- Abstractions / interfaces
- `OrderCancellationPolicy`

### OrderManagement.Domain — *Business Core*

- Entities: `Order`, `Product`, `User`, `Payment`, `OrderStatusHistory`, `InventoryMovement`
- Enums: `OrderStatus` (Pending, Confirmed, Shipped, Delivered, Cancelled), `UserRole`, `OrderCancellationReason`
- NRules facts: `OrderTransitionFact`, `CancelOrderFact`, `PaymentFact`
- Rule results: `RuleValidationResult`

### OrderManagement.Infrastructure — *Persistence & Infrastructure*

- Dapper repositories
- PostgreSQL connection factory
- Database migration runner
- JWT token generator
- BCrypt password hasher
- Current user context
- NRules integration
- Idempotency persistence & request hashing
- Activity log queue / background worker / repository

---

## Fitur Utama

- **Login** dengan JWT (BCrypt *password hashing*)
- **List & detail produk**
- **Create order** dengan *stock deduction* + idempotency protection
- **Get order detail** — termasuk histori status
- **List orders** — filter (status, customer, date range) + pagination
- **Update order status** — divalidasi oleh NRules + `row_version`
- **Cancel order** — via dedicated endpoint dengan *stock restore policy*
- **Payment mock** — *success* mengkonfirmasi order, *failed* tidak mengubah status
- **Activity log async queue** — *background worker batch insert*
- **Internal logs API & page** untuk *tracing* operasional
- **Consistent error response** — format JSON seragam
- **Database migration runner** — auto-apply di startup

---

## Siklus Hidup Order

Transisi status yang valid:

```
Pending   -> Confirmed
Pending   -> Cancelled
Confirmed -> Shipped
Confirmed -> Cancelled
Shipped   -> Delivered
Delivered -> terminal (tidak bisa diubah lagi)
Cancelled -> terminal (tidak bisa diubah lagi)
```

**PENTING:** Status `Cancelled` **tidak bisa** di-*set* melalui `PATCH /status`. Wajib menggunakan `POST /cancel` agar *stock restore*, *payment refund marker*, *inventory movement*, dan *status history* ditangani dengan benar.

### Validasi Transisi (3 Layer)

1. **Domain Entity** (`Order.cs`) — method `CanBeCancelled()`, `IsTerminal()`, `ChangeStatus()`, `Cancel()` sebagai *hard guard*.
2. **NRules Engine** — 7 aturan eksplisit:
   - `PendingToConfirmedRule`
   - `ConfirmedToShippedRule`
   - `ShippedToDeliveredRule`
   - `PendingToCancelledRule`
   - `ConfirmedToCancelledRule`
   - `CancelAllowedRule` — *cancel eligibility*
   - `TerminalOrderStateRule` — *guard* untuk mencegah transisi dari *terminal state*
3. **Application Layer** — validators & service layer mencegah cancel via `UpdateStatus`, memaksa menggunakan cancel endpoint.

### Alasan Cancel

| Reason | Restore Stock? |
|---|---|
| `CustomerRequested` | Ya |
| `OperationalIssue` | Ya |
| `FraudSuspected` | Ya |
| `StockUnavailable` | Tidak |
| `InventoryMismatch` | Tidak |

**Mengapa ada yang tidak restore stock?** Jika admin cancel karena stok fisik sudah habis terjual *offline* atau ada ketidakcocokan *warehouse*, sistem *stock* tidak boleh ditambah kembali agar tidak *overstate* ketersediaan fisik.

---

## Idempotency

### Masalah
*User* *double-click* atau *client retry* menyebabkan *request* yang sama terkirim berkali-kali, berpotensi menciptakan banyak order.

### Solusi

Setiap `POST /api/v1/orders` **wajib** menyertakan:

```http
Idempotency-Key: {unique-key}
```

### Cara Kerja

| Skenario | Hasil |
|---|---|
| Key baru + *payload* baru | *Request* diproses, order dibuat |
| Key sama + *payload* sama + *completed* | *Response* tersimpan dikembalikan |
| Key sama + *payload* sama + *in progress* | `409 REQUEST_ALREADY_IN_PROGRESS` |
| Key sama + *payload* berbeda | `409 IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD` |

### Uniqueness Scope

```sql
UNIQUE (user_id, key, endpoint)
```

- Key user A tidak bentrok dengan user B.
- Key yang sama di *endpoint* berbeda tidak bentrok.

### Request Hash

`SHA-256` dari *normalized JSON payload* untuk mendeteksi perubahan *payload*.

### Race Condition Idempotent Create

Dua *request* dengan key sama datang bersamaan:

```
Request A INSERT InProgress -> sukses (satu-satunya yang proses)
Request B INSERT gagal (unique constraint) -> baca existing record -> return conflict / stored response
```

**Hasil:** Hanya satu order yang dibuat, hanya satu *stock deduction*.

### Justifikasi Pilihan Strategi Idempotency

**Mengapa menggunakan Idempotency-Key Header + Payload Hash + Unique Constraint Database?**
- **Kelebihan dibanding Caching (Redis)**: Menyimpan idempotency state di database (PostgreSQL) memberikan jaminan *atomic transaction*. Jika transaksi pembuatan order di-*rollback*, state idempotency juga dapat diselaraskan. Dengan Redis, kita rentan terhadap *split-brain* atau koneksi terputus yang menyebabkan state *out-of-sync* dengan database.
- **Kelebihan Payload Hash**: Memastikan *client* tidak secara tidak sengaja me-*reuse* key yang sama untuk *request body* yang berbeda. Ini mencegah *silent bugs* dimana order berbeda tertelan oleh key yang sama.
- **Kelebihan Unique Constraint (`ON CONFLICT`)**: Pendekatan *atomic insert* jauh lebih superior dibanding pola *Check-Then-Insert* di level aplikasi yang rawan terhadap *Time-Of-Check to Time-Of-Use* (TOCTOU) bug saat *race condition* ekstrim.

---

## Concurrency Handling

### 1. Concurrent Stock Deduction

**Masalah:** Dua user memesan produk yang sama secara bersamaan, stok bisa minus atau *oversell*.

**Solusi:** `SELECT ... FOR UPDATE` + `ORDER BY id`

```sql
SELECT id, stock_quantity, price
FROM products
WHERE id = ANY(@ProductIds)
ORDER BY id
FOR UPDATE;
```

Setelah *lock* diperoleh:
1. Baca stok terbaru
2. Validasi stok cukup
3. *Deduct* stok
4. *Insert inventory movement*
5. *Commit*

**Deterministic lock ordering** (`ORDER BY id`) mencegah *deadlock*.

**Last defense:** `CHECK (stock_quantity >= 0)` di tabel `products`.

#### Justifikasi: Kenapa Pessimistic Locking (`SELECT FOR UPDATE`)?
- **Alasan**: *Inventory/Stock deduction* adalah area *high-contention*. Banyak *user* sering memperebutkan stok barang yang sama (flash sale).
- **Kelebihan dibanding Optimistic Locking**: Jika kita memakai *Optimistic Locking* (cek versi lalu update), banyak transaksi akan gagal (Conflict/409) dan *client* harus *retry* berulang kali, yang memperparah *load* server. Dengan *Pessimistic Locking*, transaksi otomatis *queued* di level database dan diserialisasi dengan efisien.
- **Kelebihan dibanding Memory Queue**: Menjaga arsitektur tetap *stateless* tanpa perlu *service inventory* terpisah atau *message broker* seperti Kafka/RabbitMQ untuk *prototype* ini.

### 2. Concurrent Status Update

**Masalah:** Dua admin meng-*update* status order yang sama bersamaan.

**Solusi:** `SELECT ... FOR UPDATE` + `row_version` + NRules

```sql
SELECT id, status, row_version
FROM orders
WHERE id = @OrderId
FOR UPDATE;
```

Setelah *lock*:
1. Baca *latest status*
2. Bandingkan `expectedRowVersion`
3. Jalankan NRules berdasarkan *latest status*
4. *Update* status + increment `row_version`
5. *Insert status history*

#### Justifikasi: Kenapa Optimistic Locking (`row_version`)?
- **Alasan**: Frekuensi admin saling bentrok memperbarui order yang persis *sama* relatif rendah (*low-contention*), tetapi dampaknya fatal jika ditimpa (lost update).
- **Kelebihan dibanding Pessimistic Locking**: Tidak memblokir pembacaan data, *overhead* jauh lebih ringan. Saat admin B mencoba *update* setelah admin A, wajar jika kita langsung me-*reject* dengan *error 409 Conflict* agar admin B me-*refresh* UI-nya dan melihat perubahan dari admin A.
- **Kelebihan NRules Engine**: Memisahkan validasi *state machine* yang rumit (transisi, role akses, prasyarat cancel) dari infrastruktur database.

### 3. Double Cancel Race

**Masalah:** Dua *request* cancel order yang sama bisa menyebabkan *stock restore* dua kali.

**Solusi:** Order row di-*lock* `FOR UPDATE`, `row_version` dicek, NRules hanya mengizinkan cancel dari status `Pending` / `Confirmed`. Hanya cancel pertama yang berhasil.

### 4. Payment vs Cancel Race

**Skenario:** *Payment request* dan *cancel request* masuk bersamaan untuk order yang sama. Keduanya *lock* row order yang sama.

| Payment Menang | Cancel Menang |
|---|---|
| Payment *lock* order duluan | Cancel *lock* order duluan |
| Payment *success* → `Paid` → `Confirmed` | Cancel *success* → `Cancelled` |
| Cancel menunggu, baca `Confirmed`, cancel tetap boleh | Payment menunggu, baca `Cancelled`, NRules tolak payment |
| Payment yang sudah `Paid` ditandai `RefundRequired` | Payment gagal, order tetap `Cancelled` |

### 5. Duplicate Payment Prevention

Berlapis:
1. Order row `FOR UPDATE`
2. Cek existing `Paid` payment
3. NRules validasi
4. *Partial unique index* `WHERE status = 'Paid'`

### Lock Timeout

Semua *critical transaction* men-*set*:

```sql
SET LOCAL lock_timeout = '5s';
```

Agar *request* tidak menggantung terlalu lama saat menunggu *lock*.

---

## Alasan Memilih PostgreSQL

Dibandingkan MySQL atau SQL Server, PostgreSQL dipilih dengan alasan teknis berikut:

| Alasan | Detail |
|---|---|
| Pendekatan Row-level locking yang Superior (`FOR UPDATE`) | Sangat stabil dan bisa dikombinasikan dengan `SKIP LOCKED` atau `NOWAIT` jika kelak diperlukan untuk *queueing*. Mencegah *dirty reads* secara esensial untuk *concurrency control* (Skenario A & B). |
| Unique constraint | Vital untuk idempotency |
| Check constraint | *Last defense* stok tidak negatif |
| Sequence | Sangat efisien untuk *order number generation* yang aman dan atomik di tengah *concurrent request*. Jauh lebih cepat dari tabel *counter* biasa. |
| JSONB | Dukungan *native* untuk `activity_logs` metadata/before/after state tanpa mem-parsing manual. Sangat superior dibandingkan kolom teks JSON di RDBMS lain. |
| Maturity & MVCC | Implementasi *Multi-Version Concurrency Control* (MVCC) PostgreSQL memungkinkan pembaca (GET /orders) tidak memblokir penulis (POST /orders), menjaga performa tetap tinggi. |
| Testcontainers support | Sangat mudah di-spin up untuk *integration test*, menjamin paritas 100% antara *testing* dan *production*. |

---

## Error Handling

Semua *error response* menggunakan format yang konsisten:

```json
{
  "error": {
    "code": "ERROR_CODE",
    "message": "Human readable message.",
    "details": [...],
    "correlationId": "trace-id",
    "timestamp": "2026-06-17T00:00:00Z"
  }
}
```

### HTTP Status Code Mapping

| Status | Penggunaan |
|---|---|
| `400 Bad Request` | Header tidak valid, *malformed request* |
| `401 Unauthorized` | Token tidak ada / tidak valid / kredensial salah |
| `403 Forbidden` | Terautentikasi tapi tidak diizinkan |
| `404 Not Found` | Entitas tidak ditemukan |
| `409 Conflict` | Konflik idempotency, stok, `row_version` |
| `422 Unprocessable Entity` | Validasi bisnis gagal, transisi tidak valid |
| `500 Internal Server Error` | *Error* tak terduga |

### Exception Hierarchy

```
AppException
├── ValidationAppException    → 422
├── NotFoundAppException      → 404
├── ConflictAppException      → 409
├── BusinessRuleAppException  → 422
├── UnauthorizedAppException  → 401
├── ForbiddenAppException     → 403
└── ConcurrencyAppException   → 409
```

Data sensitif (password, token, *connection string*, *stack trace*) tidak pernah bocor ke *response*.

---

## Logging & Tracing

### Correlation ID

Setiap *request* memiliki `X-Correlation-ID`:
- Jika client mengirim, API pakai itu.
- Jika tidak, API *generate* otomatis.
- Selalu dikembalikan di *response* & dicatat di *log*.

### Dua Jenis Logging

**1. Technical Logs** (Serilog / `ILogger`)
- *Request started/completed*
- *Unhandled exception*
- *Database migration*
- *Background worker failure*

**2. Activity Logs** (tabel `activity_logs` via async queue)
- *Business event*: `OrderCreated`, `StockDeducted`, `PaymentPaid`, `OrderCancelled`
- Ditulis ke *bounded channel*, diproses oleh *background worker* secara *batch*
- Tidak memperlambat *request path*
- Bisa di-*search* via Internal Logs API

### Internal Logs API

```
Admin/Ops:
  GET /api/v1/internal/activity-logs
  GET /api/v1/internal/activity-logs/{id}
  GET /internal/activity-logs (HTML page)
```

Filter: `correlationId`, `orderId`, `orderNumber`, `activityType`, `actorUserId`, `fromDate`, `toDate`, `page`, `pageSize`.

Customer tidak bisa mengakses *internal logs*.

### Activity Event Types

`RequestCompleted`, `RequestFailed`, `LoginSucceeded`, `LoginFailed`, `OrderCreated`, `OrderStatusChanged`, `OrderCancelled`, `StockDeducted`, `StockRestored`, `StockNotRestored`, `PaymentPaid`, `PaymentFailed`, `PaymentRefundRequired`, `ConcurrencyConflict`, dan lainnya (~30 tipe *event*).

---

## Database Migration

Migration disimpan di `db/migrations/`.

Saat aplikasi *start*:
1. Baca file migration.
2. Buat tabel `schema_migrations` jika belum ada.
3. Aplikasikan migration yang belum dijalankan (urut berdasarkan nama file).
4. Simpan *checksum*.
5. Jika file migration yang sudah dijalankan berubah, *startup gagal*. Buat migration baru.

---

## Race Conditions yang Dicover

1. **Concurrent stock deduction** — `SELECT FOR UPDATE` + `ORDER BY id`
2. **Concurrent status update** — `SELECT FOR UPDATE` + `row_version` + NRules
3. **Idempotent create race** — *Unique constraint* `(user_id, key, endpoint)`
4. **Double cancel** — `SELECT FOR UPDATE` + `row_version` + NRules
5. **Payment vs cancel** — Sama-sama `SELECT FOR UPDATE` pada row order yang sama
6. **Duplicate payment** — Partial unique index + NRules + existing payment check
7. **Offline/manual stock mismatch cancellation** — *Cancellation reason policy*

---

## Endpoint API

### Auth

| Method | Endpoint | Akses |
|---|---|---|
| `POST` | `/api/v1/auth/login` | Anonymous |

### Products

| Method | Endpoint | Akses |
|---|---|---|
| `GET` | `/api/v1/products` | Authenticated |
| `GET` | `/api/v1/products/{id}` | Authenticated |

### Orders

| Method | Endpoint | Akses |
|---|---|---|
| `POST` | `/api/v1/orders` | Authenticated (wajib `Idempotency-Key`) |
| `GET` | `/api/v1/orders/{id}` | Authenticated |
| `GET` | `/api/v1/orders` | Authenticated |
| `PATCH` | `/api/v1/orders/{id}/status` | Admin/Ops only |
| `POST` | `/api/v1/orders/{id}/cancel` | Customer owner / Admin / Ops |

### Payments

| Method | Endpoint | Akses |
|---|---|---|
| `POST` | `/api/v1/orders/{id}/payments` | Customer owner / Admin / Ops |
| `GET` | `/api/v1/orders/{id}/payments` | Customer owner / Admin / Ops |

### Internal

| Method | Endpoint | Akses |
|---|---|---|
| `GET` | `/api/v1/internal/activity-logs` | Admin/Ops only |
| `GET` | `/api/v1/internal/activity-logs/{id}` | Admin/Ops only |
| `GET` | `/internal/activity-logs` | Admin/Ops only (HTML page) |

### Utility

| Method | Endpoint | Akses |
|---|---|---|
| `GET` | `/health` | Anonymous |
| `GET` | `/swagger` | Development |

---

## Role & Authorization

| Role | Akses |
|---|---|
| **Buyer** | Login, lihat produk, buat order sendiri, lihat order sendiri, cancel order sendiri, bayar order sendiri |
| **SellerAdmin** | Login, lihat produk, buat/lihat/update status semua order, cancel semua order |
| **SellerOperator** | Login, lihat produk, buat/lihat/update status semua order, cancel semua order |
| **ApplicationAdmin** | *Full access* — login, produk, semua operasi order, internal logs |
| **DevOps** | Login, lihat produk — terbatas, hanya *observability* |

### Data Isolation

Customer hanya bisa melihat & mengelola order miliknya sendiri. Admin/Ops bisa melihat semua order.

---

## Run Lokal

### Opsi 1: Direct Run

Set *connection string* di `src/OrderManagement.Api/appsettings.Development.json`:

```bash
dotnet restore
dotnet build
dotnet run --project src/OrderManagement.Api/OrderManagement.Api.csproj
```

### Opsi 2: Convenience Script (Rekomendasi)

```bash
./scripts/run-api.sh
```

### Opsi 3: Navigasi ke Project

```bash
cd src/OrderManagement.Api
dotnet run
```

**Swagger:** `/swagger`
**Health:** `/health`

---

## Setup Database

### Convenience Script

```bash
./scripts/reset-db.sh
```

### Manual Seed

```bash
PGPASSWORD=order_password psql -h localhost -p 5432 -U order_user -d order_management_test -f db/seed/001_seed_users.sql
PGPASSWORD=order_password psql -h localhost -p 5432 -U order_user -d order_management_test -f db/seed/002_seed_products.sql
```

### Default Users

| Username | Password | Role |
|---|---|---|
| `appadmin` | `Password123!` | ApplicationAdmin — *full access* |
| `devops` | `Password123!` | DevOps — *observability only* |
| `selleradmin1` | `Password123!` | SellerAdmin — *mengelola store* |
| `buyer1` | `Password123!` | Buyer — *bisa create order* |
| `buyer2` | `Password123!` | Buyer — *bisa create order* |

---

## Run Tests

```bash
# Unit test
dotnet test tests/OrderManagement.Tests/OrderManagement.Tests.csproj

# Integration test (butuh Docker)
dotnet test tests/OrderManagement.IntegrationTests/OrderManagement.IntegrationTests.csproj

# Semua test
dotnet test
```

*Integration test* menggunakan Testcontainers PostgreSQL — **wajib** Docker engine berjalan.

### Cakupan Test

**Unit Test:**
- Validasi transisi status NRules (semua transisi valid & invalid)
- Validasi cancel eligibility (dari tiap status)
- Payment rules
- Cancellation policy (restore/no-restore by reason)
- Idempotency request hash
- Idempotency service behavior

**Integration Test:**
- Concurrent stock deduction
- Idempotent create race
- Concurrent status update
- Payment vs cancel race
- Duplicate payment prevention

---

## Daftar Dokumen

Dokumentasi lengkap tersedia di folder `docs/`:

| Dokumen | Deskripsi |
|---|---|
| `docs/Background.md` | Latar belakang masalah & requirements (fungsional & non-fungsional) |
| `docs/api-contract.md` | Kontrak API lengkap semua endpoint, request/response, error codes |
| `docs/architecture-diagram.md` | Diagram arsitektur: system context, layer, pipeline, database schema, sequence diagram |
| `docs/technical-design.md` | Desain teknis: struktur solusi, modul, critical transactions, security, observability |
| `docs/concurrency-design.md` | Desain concurrency: stock deduction, status update, payment vs cancel, deadlock prevention |
| `docs/idempotency-design.md` | Desain idempotency: header, database table, flow, race handling |
| `docs/error-handling.md` | Desain error handling: exception types, HTTP mapping, error codes, security rules |
| `docs/logging-design.md` | Desain logging: correlation ID, technical vs activity logs, queue architecture |
| `docs/hardening-notes.md` | Catatan hardening: order number, security, payment, cancellation |
| `docs/test-report.md` | Catatan test: cakupan unit test & integration test, key assertions |
| `docs/demo-script.md` | Script demo: curl commands untuk semua flow |
| `docs/1.Planning-Batch/` | Batch planning (13 batch untuk development bertahap) |
| `docs/2.Advanced-Batch/` | Advanced batch (4 batch untuk fitur lanjutan) |
| `docs/Advanced-Roles/` | Advanced roles (6 batch untuk role-based access) |

---

## Keterbatasan

- *Payment provider* masih *mock*.
- *Inventory service* masih *embedded* di API untuk *prototype*.
- Belum ada *distributed message broker*.
- Belum ada *outbox pattern*.
- Belum ada *refresh token*.
- Idempotency dan *order creation* masih dalam transaksi terpisah (belum *shared UnitOfWork*).

### Rencana Improvement ke Depan

- *Shared UnitOfWork* untuk idempotency + order *creation transaction*
- *Outbox pattern* untuk *integration events*
- OpenTelemetry *distributed tracing*
- *Rate limiting* untuk *login* dan *create order*
- *Refresh token*
- *Activity logs retention & partitioning*
- *External identity provider*
