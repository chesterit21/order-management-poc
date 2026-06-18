---
trigger: always_on
---

# 🛡️ QODER LINGMA: TECHNICAL RULESET (Production-Grade)

## 1. PERFORMANCE & CODE IMPLEMENTATION
*   **Primary Constructors**: Wajib digunakan untuk Dependency Injection di Services, Controllers, dan Repositories untuk mengurangi boilerplate (fitur .NET 8+ yang tetap optimal di .NET 10).
*   **Immutability**: Gunakan `record` untuk semua DTOs, Commands, Queries, dan Response Contracts. Ini menjamin thread-safety dan mengurangi alokasi memori.
*   **Async/Await Discipline**: 
    *   Semua I/O operations (DB, HTTP, File) WAJIB `async`/`await`.
    *   `CancellationToken` WAJIB di-passing dari Controller sampai ke Repository untuk mendukung graceful shutdown.
    *   DILARANG menggunakan `.Result` atau `.Wait()` (akan menyebabkan thread pool starvation/deadlock).
*   **String Handling**: Gunakan string interpolation `$"{}"` atau `StringBuilder` untuk operasi string kompleks. Hindari `string.Concat` atau `+` di dalam loop.
 to avoid unnecessary allocations.
*   **No LINQ-to-Objects Overload**: Di hot-path (misal: mapping besar), gunakan Dapper's native mapping atau loop `foreach` eksplisit daripada chained LINQ yang berat.

## 2. SECURITY HIGH-LEVEL
*   **SQL Injection Prevention**: DILARANG KERAS melakukan string concatenation untuk query SQL. WAJIB menggunakan Dapper Parameterized Queries (`@paramName`).
*   **Data Exposure**: DILARANG mengembalikan Entity Domain langsung ke API Response. WAJIB map ke DTO. Pastikan field sensitif (`PasswordHash`, `InternalId`) tidak pernah terpapar.
*   **JWT Validation**: Token WAJIB divalidasi `Issuer`, `Audience`, dan `Expiration`. Gunakan `TokenValidationParameters` yang ketat.
*   **CORS & Headers**: WAJIB konfigurasi CORS secara eksplisit (no `AllowAnyOrigin` di production). WAJIB tambahkan header keamanan dasar (meski POC, tunjukkan awareness).
*   **Secrets Management**: DILARANG hardcode connection string atau JWT Secret di code. WAJIB bind via `IOptions<T>` dari `appsettings.json` atau Environment Variables.

## 3. GLOBAL EXCEPTION HANDLING
*   **Single Source of Truth**: Hanya ada **satu** `GlobalExceptionHandlingMiddleware` di pipeline API.
*   **Strict Error Envelope**: Semua error WAJIB mengembalikan format JSON persis seperti ini:
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
*   **No Stack Trace Leakage**: DILARANG mengembalikan `Exception.Message` atau `StackTrace` asli ke client (kecuali di environment `Development` dengan flag khusus). Internal error WAJIB di-log sebagai `LogLevel.Error` dengan full stack trace.
*   **Custom Exceptions Mapping**:
    *   `ValidationAppException` → `400 Bad Request` atau `422 Unprocessable Entity`
    *   `NotFoundAppException` → `404 Not Found`
    *   `ConcurrencyAppException` / `ConflictAppException` → `409 Conflict`
    *   `BusinessRuleAppException` → `422 Unprocessable Entity`
    *   `UnauthorizedAppException` → `401 Unauthorized`
    *   `Unhandled Exception` → `500 Internal Server Error`

## 4. LOGGING & OBSERVABILITY (Serilog)
*   **Structured Logging ONLY**: DILARANG menggunakan string concatenation di log. WAJIB gunakan template: `Log.Information("Order {OrderId} created with total {TotalAmount}", orderId, total)`.
*   **Mandatory Enrichers**: Setiap log WAJIB di-enrich dengan: `CorrelationId`, `UserId`, `RequestMethod`, `RequestPath`, `ElapsedMs`.
*   **PII/Sensitive Data Masking**: DILARANG log `Password`, `JWT Token`, atau full request body yang berisi data sensitif. Untuk `Idempotency-Key`, log hanya prefix-nya (misal: `6f91fca4...`).
*   **Log Levels**:
    *   `Information`: Business flow sukses (Order Created, Payment Success).
    *   `Warning`: Business rule rejection yang valid (Insufficient Stock, Invalid Status Transition).
    *   `Error`: Unhandled exceptions, Database connection failures.

## 5. QUERY OPTIMIZATION & DATABASE (Dapper + PostgreSQL)
*   **Explicit SQL Mapping**: WAJIB map `snake_case` (DB) ke `PascalCase` (C#) secara eksplisit di SQL menggunakan `AS`. Contoh: `SELECT created_at AS CreatedAt, row_version AS RowVersion`. Jangan mengandalkan auto-mapper ajaib yang menyembunyikan query.
*   **Deadlock Prevention (HUKUM MUTLAK)**: Setiap kali melakukan `SELECT ... FOR UPDATE` pada multiple rows (misal: multiple products dalam satu order), query WAJIB diurutkan: `ORDER BY id ASC`. Ini mencegah circular wait.
*   **Transaction Scope**: `BEGIN TRANSACTION` WAJIB dibuka selambat mungkin dan di-`COMMIT`/`ROLLBACK` secepat mungkin. Jangan lakukan I/O eksternal (HTTP call) di dalam transaction.
*   **Batch Operations**: Untuk insert multiple `order_items` atau `inventory_movements`, WAJIB gunakan Dapper's `ExecuteAsync` dengan `IEnumerable` untuk mengurangi round-trip ke database.
*   **Index Awareness**: Pastikan setiap kolom di `WHERE` clause memiliki index yang sesuai (sudah didefinisikan di migration script).

 **CACHE MANAGEMENT**
*   **Read-Through Only**: Cache HANYA untuk data yang bersifat read-heavy dan slowly changing (misal: `GET /api/products`).
*   **Strict Invalidation**: Setiap ada update/delete pada `Product`, cache WAJIB di-invalidate secara eksplisit.
*   **NO CACHE FOR**: Orders, Inventory, Payments, Idempotency Keys. Data ini WAJIB *Strongly Consistent* dan harus selalu dibaca langsung dari DB dengan locking yang tepat.
*   Gunakan `IMemoryCache` dengan `AbsoluteExpirationRelativeToNow` untuk POC ini (sebutkan Redis sebagai upgrade path di README).

## 7. API CONTROLLER & REQUEST VALIDATION
*   **Dumb Controllers**: Controller HANYA bertugas: 1) Terima Request, 2) Panggil Application Service, 3) Return Response. DILARANG ada business logic, DB call, atau NRules call di Controller.
*   **FluentValidation**: Semua validasi input WAJIB dipisah ke class `IValidator<T>` di layer Application.
*   **Auto-Validation**: Gunakan `FluentValidation.AspNetCore` atau custom `ActionFilter` agar validasi terjadi otomatis sebelum masuk ke method controller.
*   **Idempotency Enforcement**: `POST /api/v1/orders` WAJIB dilindungi oleh `RequireIdempotencyKeyFilter` yang memvalidasi keberadaan dan format header sebelum eksekusi berlanjut.

## 8. BUSINESS PROCESS VALIDATION (Application Layer)
*   **CQRS Pattern**: Pisahkan `Command` (Write) dan `Query` (Read). Jangan gunakan satu DTO untuk keduanya.
*   **Fail Fast**: Validasi precondition yang murah (misal: format data, user existence) SEBELUM membuka Database Transaction.
*   **NRules Delegation**: DILARANG menulis `if/else` bertingkat untuk validasi state transition (misal: `if status == Pending && target == Confirmed`). Serahkan ini ke `IOrderRulesService` yang membungkus NRules.
*   **Idempotency Check Timing**: Pengecekan `Idempotency-Key` WAJIB terjadi di **awal** transaction, sebelum ada data yang di-modifikasi.

## 9. DOMAIN MODEL & VALIDATION RULES
*   **Rich Domain, Anemic Infrastructure**: Entity WAJIB menjaga invariant-nya sendiri. Contoh: Method `Order.Cancel()` harus memvalidasi apakah status saat ini memungkinkan pembatalan (bisa delegate ke NRules fact).
*   **Value Objects**: Gunakan Value Object untuk konsep yang memiliki aturan bisnis intrinsik. Contoh: `Money` (memastikan `Amount >= 0`), `OrderNumber` (memastikan format `ORD-YYYYMMDD-XXXXXX`).
*   **Zero External Dependencies**: Layer `Domain` DILARANG memiliki reference ke `Microsoft.EntityFrameworkCore`, `Dapper`, `Serilog`, atau `ASP.NET Core`. Murni C# dan NRules (untuk Facts).
*   **Enums**: Gunakan C# Enums dengan nilai integer eksplisit, atau strongly-typed Enums (jika butuh behavior kompleks).

## 10. DOMAIN MODEL TESTING & QUALITY ASSURANCE
*   **Unit Tests for NRules**: WAJIB ada test yang membuktikan setiap allowed transition (e.g., Pending → Confirmed) dan denied transition (e.g., Shipped → Cancelled).
*   **Unit Tests for Value Objects**: WAJIB test boundary conditions (e.g., membuat `Money` dengan nilai negatif harus throw `ArgumentException`).
*   **Integration Tests with Testcontainers**: DILARANG mock database untuk concurrency tests. WAJIB gunakan `Testcontainers.PostgreSql` untuk mensimulasikan race condition yang sebenarnya (misal: 2 Task paralel mencoba deduct stock yang sama).
*   **AAA Pattern**: Semua test WAJIB mengikuti struktur `Arrange`, `Act`, `Assert` yang jelas.
*   **Naming Convention**: Test method WAJIB deskriptif: `Should_ThrowConcurrencyException_When_TwoRequestsUpdateSameOrder()`.

---

### 🎯 EXECUTION PROTOCOL (Cara Gue Bekerja)

1. **No Hallucination**: Gue nggak bakal nge-claim sebuah library bisa melakukan sesuatu kalau dokumentasi resminya nggak bilang begitu.
2. **Complete Files**: Saat gue generate code, gue akan berikan file yang **lengkap** (termasuk `using` statements), siap copy-paste, bukan potongan snippet yang membingungkan.
3. **Step-by-Step**: Gue akan menunggu konfirmasi lu per Fase. Gue nggak akan nge-generate 50 file sekaligus. Kita akan fokus per layer/component agar review mudah.
4. **Blueprint Adherence**: Setiap line of code yang gue tulis akan diverifikasi mentally terhadap `poc-oms.md` (terutama soal `FOR UPDATE`, `ORDER BY id`, dan Idempotency flow).

---

#### 1. MODERN C# & .NET 10.0.9 ENFORCEMENT
*   **LangVersion**: Wajib `<LangVersion>latest</LangVersion>` di `Directory.Build.props`.
*   **Primary Constructors**: WAJIB digunakan untuk Dependency Injection di semua Class (Controllers, Services, Repositories) untuk menghilangkan boilerplate.
    *   *Contoh*: `public class OrderService(IOrderRepository repo, ILogger<OrderService> logger) { ... }`
*   **Collection Expressions**: Gunakan syntax `[]` untuk inisialisasi collection, bukan `new List<T>()` atau `new T[] {}`.
    *   *Contoh*: `List<string> roles = ["Admin", "Ops"];`
*   **`required` Modifier**: WAJIB digunakan pada properti DTO/Command yang tidak boleh null, menggantikan validasi null manual yang bertele-tele.
*   **DateOnly & TimeOnly**: WAJIB digunakan untuk properti tanggal/waktu di Domain/DTO (bukan `DateTime`). Dapper `Npgsql` mapping akan dikonfigurasi untuk menangani ini secara native tanpa konversi `DateTime` yang rawan bug timezone.
*   **Pattern Matching**: Gunakan advanced pattern matching (e.g., `switch` expressions, property patterns) untuk meningkatkan readability logika bisnis, terutama di NRules atau State Validation.

#### 2. HIGH-PERFORMANCE CODE GUIDELINES
*   **JSON Serialization**: DILARANG menggunakan default `System.Text.Json` reflection-based serialization di *hot paths* (seperti Idempotency Response caching atau high-traffic endpoints). WAJIB gunakan **Source Generators** (`[JsonSerializable]`) atau library source-generated seperti **Riok.Mapperly** untuk mapping, bukan AutoMapper (yang berat di runtime).
*   **String Handling**: Gunakan `ReadOnlySpan<char>` untuk operasi parsing atau substring sederhana guna menghindari alokasi string di heap.
*   **Async/Await**: 
    *   Gunakan `ValueTask<T>` alih-alih `Task<T>` untuk method yang sering *cache hit* atau synchronous completion (misal: validasi idempotency key yang ditemukan di memory cache sebelum ke DB).
    *   Selalu pass `CancellationToken` dari Controller hingga ke Dapper `QueryAsync`/`ExecuteAsync`.
*   **Dapper Optimization**: 
    *   Gunakan `CommandDefinition` dengan `CancellationToken` dan `CommandType.Text`.
    *   Untuk mapping `snake_case` ke `PascalCase`, gunakan alias eksplisit di SQL (`SELECT created_at AS CreatedAt`) atau konfigurasi `DefaultTypeMap.MatchNamesWithUnderscores = true` **sekali** di startup, jangan di setiap query.

#### 3. SECURITY & JWT INDUSTRY STANDARD (Production Grade)
*   **JWT Algorithm**: Untuk POC, gunakan **HS256** dengan secret minimal **256-bit (32 karakter acak)**, tetapi arsitektur `JwtOptions` WAJIB disiapkan untuk mendukung **RS256 (Asymmetric)** di masa depan (dengan `SecurityKey` abstraction).
*   **Mandatory JWT Claims**: Token WAJIB mengandung:
    *   `sub` (Subject / User ID)
    *   `jti` (JWT ID - Unique identifier per token, **wajib** untuk fitur revocation/blacklist di production).
    *   `iat` (Issued At)
    *   `exp` (Expiration Time)
    *   `role` (Custom claim untuk Authorization)
*   **Token Validation Parameters**: WAJIB set:
    *   `ValidateLifetime = true`
    *   `ValidateIssuer = true`
    *   `ValidateAudience = true`
    *   `RequireSignedTokens = true`
    *   `ClockSkew = TimeSpan.Zero` (Strict expiration, tidak ada toleransi detik untuk POC financial/order system).
*   **Password Hashing**: WAJIB gunakan `BCrypt.Net-Next` dengan **Work Factor minimal 12** (bukan default 10) untuk melindungi terhadap brute-force.
*   **Security Headers**: Middleware WAJIB menambahkan header dasar: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`.

#### 4. MAINTAINABILITY & READABILITY (Clean over Clever)
*   **Immutability by Default**: Semua DTO, Command, Query, dan Response WAJIB berupa `record`. Ini menjamin thread-safety dan membuat kode lebih mudah dipahami (tidak ada side-effect tersembunyi).
*   **No AutoMapper**: Gunakan **Explicit Mapping** (manual `new Record(...)`) atau **Riok.Mapperly** (Source Generator). AutoMapper terlalu berat dan menyembunyikan mapping logic, menyulitkan debugging dan performa.
*   **Fail Fast Validation**: Letakkan validasi termurah (format, required) di paling awal method sebelum membuka transaksi atau melakukan I/O.
*   **Meaningful Naming**: 
    *   Command: `CreateOrderCommand` (bukan `CreateOrderRequest` di layer Application).
    *   Result: `CreateOrderResult` (bukan `Response`).
    *   Hindari singkatan tidak standar (gunakan `Id`, bukan `ID`; `OrderId`, bukan `OID`).

#### 5. MODERN PACKAGE STACK (.NET 10.0.9 COMPATIBLE)
Pastikan versi package adalah yang terbaru dan stabil untuk .NET 8/10 ecosystem:
*   **Data Access**: `Dapper` (v2.1.x+), `Npgsql` (v8.0.x+).
*   **Validation**: `FluentValidation` (v11.9.x+), `FluentValidation.AspNetCore`.
*   **Logging**: `Serilog.AspNetCore` (v8.0.x+), `Serilog.Sinks.Console`, `Serilog.Enrichers.CorrelationId`.
*   **Security**: `BCrypt.Net-Next` (v4.0.x+), `System.IdentityModel.Tokens.Jwt` (v7.5.x+), `Microsoft.AspNetCore.Authentication.JwtBearer`.
*   **Mapping (Optional but Recommended)**: `Riok.Mapperly` (v3.5.x+) untuk source-generated mapping yang ultra-cepat.
*   **Rules**: `NRules` (v0.9.x+).
*   **Testing**: `xunit`, `FluentAssertions` (v6.12.x+), `Moq` (v4.20.x+) atau **`NSubstitute`** (lebih modern dan clean syntax-nya), `Testcontainers.PostgreSql` (v3.8.x+).

---

**NOTE**: 
- Kamu di perbolahkan untuk merefactor code jika memang harus di refactor ke dalam code yang lebih baik dengan versi C# latest dan sesuai .NET10  target framework. Dan kamu diperbolehkan untuk menggunakan versi C# yang lebih baru serta optimize code agar lebih bagus lagi dalam penggunaan memory, performance dan security.
- Do NOT Execute Script Run "dotnet run"
- DO NOT Execute Script Run "dotnet watch run"
- DO NOT Execute Script Build "dotnet build"
- ONLY ALLOW Execute Script Build Release "dotnet build -c Release"