---
trigger: always_on
---

### 🛡️ QODER LINGMA: AI AGENT RULESET (Order Management POC)

#### 1. STRICT ARCHITECTURAL BOUNDARIES (Clean Architecture)
*   **Domain**: Murni C#, TIDAK BOLEH ada reference ke `Microsoft.EntityFrameworkCore`, `Dapper`, `Serilog`, atau `ASP.NET Core`. Hanya Entities, Enums, Value Objects, dan NRules Facts.
*   **Application**: Hanya berisi Use Cases (Services), DTOs, Validators (FluentValidation), dan Interfaces. TIDAK BOLEH ada implementasi database atau HTTP logic.
*   **Infrastructure**: Tempat implementasi Interface (Dapper Repositories, NRules Engine, JWT Generator, Password Hasher). BOLEH reference ke Domain & Application.
*   **Api**: Hanya Controller, Middleware, Filters, dan Dependency Injection setup. TIDAK BOLEH ada business logic di Controller.

#### 2. CONCURRENCY & DEADLOCK PREVENTION (Hukum Wajib)
*   Setiap query yang mengubah data (Update/Insert yang bergantung pada state) WAJIB menggunakan `BEGIN TRANSACTION`.
*   Row locking WAJIB menggunakan `FOR UPDATE`.
*   **ANTI-DEADLOCK RULE**: Setiap kali meng-lock multiple rows (misal: multiple products dalam satu order), query WAJIB diurutkan: `ORDER BY id ASC`. Ini non-negotiable untuk mencegah deadlock.
*   Gunakan `row_version` untuk optimistic concurrency check pada update status order.

#### 3. IDEMPOTENCY ENFORCEMENT
*   Endpoint `POST /api/v1/orders` WAJIB memvalidasi header `Idempotency-Key`.
*   Urutan eksekusi di dalam transaction:
    1. `INSERT INTO idempotency_keys ... ON CONFLICT DO NOTHING`
    2. Jika conflict, cek status: `Completed` (return stored response), `InProgress` (return 409), `Different Hash` (return 409).
    3. Jika baru, lanjut proses bisnis.
    4. Update `idempotency_keys` jadi `Completed` dengan `response_body` sebelum `COMMIT`.

#### 4. ERROR HANDLING & LOGGING STANDARD
*   **Global Exception Middleware** WAJIB menangkap semua exception dan mengembalikan format persis seperti ini:
    ```json
    {
      "error": {
        "code": "ERROR_CODE",
        "message": "Human readable message",
        "details": [],
        "correlationId": "01JY2A9M7KM6E7WJ7M7X2F9E3P",
        "timestamp": "2026-06-17T03:24:00Z"
      }
    }
    ```
*   **Logging**: WAJIB menggunakan Serilog structured logging. Setiap log WAJIB enrich dengan `CorrelationId`, `UserId`, `RequestMethod`, `RequestPath`.
*   DILARANG keras mengembalikan stack trace ke client (hanya log di server).

#### 5. DATABASE & DAPPER BEST PRACTICES
*   Gunakan **Explicit SQL**. Jangan pakai auto-mapper ajaib yang menyembunyikan query.
*   Mapping snake_case (DB) ke PascalCase (C#) WAJIB eksplisit di SQL: `SELECT created_at AS CreatedAt, row_version AS RowVersion`.
*   WAJIB respect `CHECK constraints` yang sudah didefinisikan (misal: `stock_quantity >= 0`).

#### 6. NRULES USAGE SCOPE
*   NRules HANYA untuk business state transition (Order Status, Cancel Eligibility, Payment Rules).
*   DILARANG pakai NRules untuk validasi dasar (null check, required field) atau database constraint. Itu tugas FluentValidation dan PostgreSQL.

#### 7. CODE QUALITY & .NET 10 FEATURES
*   Aktifkan `<Nullable>enable</Nullable>` dan `<ImplicitUsings>enable</ImplicitUsings>`.
*   Gunakan `record` untuk DTOs dan Response contracts agar immutable dan ringkas.
*   Gunakan Primary Constructors untuk Dependency Injection di Services/Repositories (fitur .NET 8+ yang tetap valid dan clean di .NET 10).

#### 8. STEP-BY-STEP EXECUTION PROTOCOL (Anti-Overwhelm)
*   AI TIDAK BOLEH men-generate seluruh solusi sekaligus.
*   AI WAJIB menunggu instruksi per Fase (misal: "Fase 1: Config & Docker", "Fase 2: Domain Entities").
*   Setiap file yang di-generate WAJIB lengkap, siap copy-paste, dan termasuk `using` statements yang diperlukan.

---
