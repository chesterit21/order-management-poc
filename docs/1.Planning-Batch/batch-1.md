Gaskeun bro. Kita mulai **implementasi code Batch 1**.

Gue bagi implementasi jadi beberapa batch biar rapi:

```text
Batch 1: Infrastructure Config Files + Migration Runner + Database Migration SQL
Batch 2: Domain Model DDD, Enums, Value Objects, Error Codes, Exceptions
Batch 3: Global Exception Handling, Correlation ID, Logging Middleware
Batch 4: Auth/Login JWT + User Repository + Password Hashing
Batch 5: Products API + Dapper Repository
Batch 6: NRules Order Lifecycle
Batch 7: Idempotency Service
Batch 8: Create Order Transaction + Stock Row Lock
Batch 9: Get/List Orders
Batch 10: Update Status + Cancel Order
Batch 11: Payment Simple Flow
Batch 12: Unit + Integration + Concurrency Tests
```

Sekarang kita kerjakan:

# Batch 1

# Infrastructure Config Files + Migration Runner + Database Migration SQL

Target Batch 1:

```text
1. appsettings base/development/testing
2. Options classes
3. Dapper/Npgsql connection factory
4. Migration runner
5. Startup auto-check/apply DB schema
6. PostgreSQL migration SQL
7. Program.cs baseline yang clean
```

> Catatan: Tidak pakai Docker Compose sesuai request lu. Connection string langsung ke PostgreSQL lokal yang sudah lu punya.

***

# 0. Tambahan Package

Karena kita pakai Serilog config console, tambahkan package ini:

```bash
dotnet add src/OrderManagement.Api/OrderManagement.Api.csproj package Serilog.Sinks.Console
```

***

# 1. `appsettings.json`

Replace file:

```text
src/OrderManagement.Api/appsettings.json
```

Dengan:

```json
{
  "Application": {
    "Name": "Order Management API",
    "DefaultTimezone": "UTC"
  },

  "Database": {
    "ConnectionString": "Host=localhost;Port=5432;Database=order_management;Username=order_user;Password=order_password;Pooling=true;Minimum Pool Size=1;Maximum Pool Size=50;Timeout=30;Command Timeout=30"
  },

  "Migration": {
    "Enabled": true,
    "Path": "db/migrations",
    "SchemaTable": "schema_migrations"
  },

  "Jwt": {
    "Issuer": "OrderManagement.Api",
    "Audience": "OrderManagement.Clients",
    "Secret": "LOCAL_DEV_ONLY_CHANGE_ME_MINIMUM_32_CHARS",
    "AccessTokenExpirationMinutes": 60
  },

  "Idempotency": {
    "HeaderName": "Idempotency-Key",
    "KeyMaxLength": 200,
    "InProgressTtlSeconds": 120,
    "CompletedRecordRetentionDays": 7,
    "FailedRecordRetentionDays": 1
  },

  "Cors": {
    "AllowedOrigins": [
      "http://localhost:3000",
      "http://localhost:4200",
      "http://localhost:5173",
      "http://localhost:5002",
      "https://localhost:5002"
    ]
  },

  "Swagger": {
    "Enabled": true,
    "Title": "Order Management API",
    "Version": "v1",
    "Description": "Prototype API with idempotency and concurrency-safe order management."
  },

  "HealthChecks": {
    "Enabled": true,
    "Path": "/health"
  },

  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.AspNetCore": "Warning",
        "System": "Warning",
        "Npgsql": "Warning"
      }
    },
    "Enrich": [
      "FromLogContext",
      "WithMachineName",
      "WithThreadId"
    ],
    "WriteTo": [
      {
        "Name": "Console"
      }
    ],
    "Properties": {
      "Application": "OrderManagement.Api"
    }
  },

  "AllowedHosts": "*"
}
```

***

# 2. `appsettings.Development.json`

Replace file:

```text
src/OrderManagement.Api/appsettings.Development.json
```

Dengan:

```json
{
  "Database": {
    "ConnectionString": "Host=localhost;Port=5432;Database=order_management;Username=order_user;Password=order_password;Pooling=true;Minimum Pool Size=1;Maximum Pool Size=50;Timeout=30;Command Timeout=30"
  },

  "Migration": {
    "Enabled": true,
    "Path": "db/migrations",
    "SchemaTable": "schema_migrations"
  },

  "Swagger": {
    "Enabled": true
  },

  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.AspNetCore": "Information",
        "System": "Warning",
        "Npgsql": "Warning",
        "OrderManagement": "Debug"
      }
    }
  }
}
```

***

# 3. `appsettings.Testing.json`

Create file:

```text
src/OrderManagement.Api/appsettings.Testing.json
```

Isi:

```json
{
  "Database": {
    "ConnectionString": "Host=localhost;Port=5432;Database=order_management_test;Username=order_user;Password=order_password;Pooling=true;Minimum Pool Size=1;Maximum Pool Size=50;Timeout=30;Command Timeout=30"
  },

  "Migration": {
    "Enabled": true,
    "Path": "db/migrations",
    "SchemaTable": "schema_migrations"
  },

  "Jwt": {
    "Issuer": "OrderManagement.Api.Tests",
    "Audience": "OrderManagement.Tests",
    "Secret": "TEST_ONLY_SECRET_MINIMUM_32_CHARS",
    "AccessTokenExpirationMinutes": 60
  },

  "Swagger": {
    "Enabled": false
  },

  "Serilog": {
    "MinimumLevel": {
      "Default": "Warning",
      "Override": {
        "OrderManagement": "Information"
      }
    }
  }
}
```

***

# 4. Application Abstraction: `IDbConnectionFactory`

Replace file:

```text
src/OrderManagement.Application/Abstractions/Database/IDbConnectionFactory.cs
```

Dengan:

```csharp
using System.Data.Common;

namespace OrderManagement.Application.Abstractions.Database;

public interface IDbConnectionFactory
{
    Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
}
```

***

# 5. Infrastructure Option Classes

## 5.1 `DatabaseOptions.cs`

Replace file:

```text
src/OrderManagement.Infrastructure/Options/DatabaseOptions.cs
```

Dengan:

```csharp
namespace OrderManagement.Infrastructure.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; init; } = string.Empty;
}
```

***

## 5.2 `MigrationOptions.cs`

Create file baru:

```text
src/OrderManagement.Infrastructure/Options/MigrationOptions.cs
```

Isi:

```csharp
namespace OrderManagement.Infrastructure.Options;

public sealed class MigrationOptions
{
    public const string SectionName = "Migration";

    public bool Enabled { get; init; } = true;

    public string Path { get; init; } = "db/migrations";

    public string SchemaTable { get; init; } = "schema_migrations";
}
```

***

## 5.3 `JwtOptions.cs`

Replace file:

```text
src/OrderManagement.Infrastructure/Options/JwtOptions.cs
```

Dengan:

```csharp
namespace OrderManagement.Infrastructure.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public string Secret { get; init; } = string.Empty;

    public int AccessTokenExpirationMinutes { get; init; } = 60;
}
```

***

## 5.4 `IdempotencyOptions.cs`

Replace file:

```text
src/OrderManagement.Infrastructure/Options/IdempotencyOptions.cs
```

Dengan:

```csharp
namespace OrderManagement.Infrastructure.Options;

public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    public string HeaderName { get; init; } = "Idempotency-Key";

    public int KeyMaxLength { get; init; } = 200;

    public int InProgressTtlSeconds { get; init; } = 120;

    public int CompletedRecordRetentionDays { get; init; } = 7;

    public int FailedRecordRetentionDays { get; init; } = 1;
}
```

***

# 6. API Option Class: `ClientCorsOptions`

Replace file:

```text
src/OrderManagement.Api/Options/CorsOptions.cs
```

Dengan:

```csharp
namespace OrderManagement.Api.Options;

public sealed class ClientCorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; init; } = [];
}
```

***

# 7. Dapper/Npgsql Connection Factory

Replace file:

```text
src/OrderManagement.Infrastructure/Database/DbConnectionFactory.cs
```

Dengan:

```csharp
using System.Data.Common;
using Microsoft.Extensions.Options;
using Npgsql;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Infrastructure.Options;

namespace OrderManagement.Infrastructure.Database;

public sealed class DbConnectionFactory : IDbConnectionFactory
{
    private readonly DatabaseOptions _databaseOptions;

    public DbConnectionFactory(IOptions<DatabaseOptions> databaseOptions)
    {
        _databaseOptions = databaseOptions.Value;
    }

    public async Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_databaseOptions.ConnectionString))
        {
            throw new InvalidOperationException("Database connection string is not configured.");
        }

        var connection = new NpgsqlConnection(_databaseOptions.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        return connection;
    }
}
```

***

# 8. Migration Runner Interface

Create file baru:

```text
src/OrderManagement.Infrastructure/Database/IDatabaseMigrationRunner.cs
```

Isi:

```csharp
namespace OrderManagement.Infrastructure.Database;

public interface IDatabaseMigrationRunner
{
    Task ApplyMigrationsAsync(CancellationToken cancellationToken = default);
}
```

***

# 9. Database Migration Runner

Create file baru:

```text
src/OrderManagement.Infrastructure/Database/DatabaseMigrationRunner.cs
```

Isi:

```csharp
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Infrastructure.Options;

namespace OrderManagement.Infrastructure.Database;

public sealed class DatabaseMigrationRunner : IDatabaseMigrationRunner
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly MigrationOptions _migrationOptions;
    private readonly ILogger<DatabaseMigrationRunner> _logger;

    public DatabaseMigrationRunner(
        IDbConnectionFactory connectionFactory,
        IOptions<MigrationOptions> migrationOptions,
        ILogger<DatabaseMigrationRunner> logger)
    {
        _connectionFactory = connectionFactory;
        _migrationOptions = migrationOptions.Value;
        _logger = logger;
    }

    public async Task ApplyMigrationsAsync(CancellationToken cancellationToken = default)
    {
        if (!_migrationOptions.Enabled)
        {
            _logger.LogInformation("Database migration is disabled.");
            return;
        }

        var migrationPath = ResolveMigrationPath(_migrationOptions.Path);

        _logger.LogInformation("Applying database migrations from path {MigrationPath}", migrationPath);

        var migrationFiles = Directory
            .GetFiles(migrationPath, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (migrationFiles.Length == 0)
        {
            _logger.LogWarning("No migration files found in path {MigrationPath}", migrationPath);
            return;
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        await EnsureSchemaMigrationTableAsync(connection, cancellationToken);

        foreach (var file in migrationFiles)
        {
            var migrationName = Path.GetFileName(file);
            var sql = await File.ReadAllTextAsync(file, cancellationToken);
            var checksum = ComputeSha256(sql);

            var existingChecksum = await connection.QuerySingleOrDefaultAsync<string>(
                new CommandDefinition(
                    commandText: $"""
                                  SELECT checksum
                                  FROM {_migrationOptions.SchemaTable}
                                  WHERE migration_name = @MigrationName
                                  """,
                    parameters: new { MigrationName = migrationName },
                    cancellationToken: cancellationToken));

            if (existingChecksum is not null)
            {
                if (!string.Equals(existingChecksum, checksum, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Migration '{migrationName}' was already applied but its checksum has changed. Create a new migration instead of modifying applied migrations.");
                }

                _logger.LogDebug("Skipping already applied migration {MigrationName}", migrationName);
                continue;
            }

            _logger.LogInformation("Applying migration {MigrationName}", migrationName);

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        commandText: sql,
                        transaction: transaction,
                        cancellationToken: cancellationToken));

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        commandText: $"""
                                      INSERT INTO {_migrationOptions.SchemaTable}
                                          (migration_name, checksum, applied_at)
                                      VALUES
                                          (@MigrationName, @Checksum, NOW())
                                      """,
                        parameters: new
                        {
                            MigrationName = migrationName,
                            Checksum = checksum
                        },
                        transaction: transaction,
                        cancellationToken: cancellationToken));

                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation("Migration {MigrationName} applied successfully", migrationName);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        _logger.LogInformation("Database migration completed.");
    }

    private async Task EnsureSchemaMigrationTableAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        var tableName = _migrationOptions.SchemaTable;

        var sql = $"""
                  CREATE TABLE IF NOT EXISTS {tableName} (
                      migration_name VARCHAR(255) PRIMARY KEY,
                      checksum VARCHAR(64) NOT NULL,
                      applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                  );
                  """;

        await connection.ExecuteAsync(
            new CommandDefinition(
                commandText: sql,
                cancellationToken: cancellationToken));
    }

    private static string ComputeSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hashBytes = SHA256.HashData(bytes);

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string ResolveMigrationPath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath) && Directory.Exists(configuredPath))
        {
            return configuredPath;
        }

        var current = Directory.GetCurrentDirectory();

        for (var i = 0; i < 10; i++)
        {
            var candidate = Path.GetFullPath(Path.Combine(current, configuredPath));

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(current);

            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        throw new DirectoryNotFoundException(
            $"Migration path '{configuredPath}' was not found. Current directory: '{Directory.GetCurrentDirectory()}'.");
    }
}
```

***

# 10. Infrastructure Dependency Injection

Replace file:

```text
src/OrderManagement.Infrastructure/DependencyInjection.cs
```

Dengan:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Infrastructure.Database;
using OrderManagement.Infrastructure.Options;

namespace OrderManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(
            configuration.GetSection(DatabaseOptions.SectionName));

        services.Configure<MigrationOptions>(
            configuration.GetSection(MigrationOptions.SectionName));

        services.Configure<JwtOptions>(
            configuration.GetSection(JwtOptions.SectionName));

        services.Configure<IdempotencyOptions>(
            configuration.GetSection(IdempotencyOptions.SectionName));

        services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
        services.AddScoped<IDatabaseMigrationRunner, DatabaseMigrationRunner>();

        return services;
    }
}
```

***

# 11. Application Dependency Injection

Replace file:

```text
src/OrderManagement.Application/DependencyInjection.cs
```

Dengan:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace OrderManagement.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        return services;
    }
}
```

***

# 12. API Migration Extension

Create file baru:

```text
src/OrderManagement.Api/Extensions/DatabaseMigrationExtensions.cs
```

Isi:

```csharp
using OrderManagement.Infrastructure.Database;

namespace OrderManagement.Api.Extensions;

public static class DatabaseMigrationExtensions
{
    public static async Task ApplyDatabaseMigrationsAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();

        var migrationRunner = scope.ServiceProvider.GetRequiredService<IDatabaseMigrationRunner>();
        await migrationRunner.ApplyMigrationsAsync();
    }
}
```

***

# 13. Program.cs Baseline

Replace file:

```text
src/OrderManagement.Api/Program.cs
```

Dengan:

```csharp
using OrderManagement.Api.Extensions;
using OrderManagement.Api.Options;
using OrderManagement.Application;
using OrderManagement.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

builder.Services.Configure<ClientCorsOptions>(
    builder.Configuration.GetSection(ClientCorsOptions.SectionName));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    var corsOptions = builder.Configuration
        .GetSection(ClientCorsOptions.SectionName)
        .Get<ClientCorsOptions>() ?? new ClientCorsOptions();

    options.AddPolicy("ClientApps", policy =>
    {
        if (corsOptions.AllowedOrigins.Length > 0)
        {
            policy.WithOrigins(corsOptions.AllowedOrigins);
        }
        else
        {
            policy.AllowAnyOrigin();
        }

        policy
            .WithMethods("GET", "POST", "PATCH", "DELETE", "OPTIONS")
            .WithHeaders("Authorization", "Content-Type", "Idempotency-Key", "X-Correlation-ID")
            .WithExposedHeaders("X-Correlation-ID", "Location");
    });
});

builder.Services.AddHealthChecks();

var app = builder.Build();

await app.ApplyDatabaseMigrationsAsync();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();

app.UseHttpsRedirection();

app.UseCors("ClientApps");

app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health");

app.Run();

public partial class Program;
```

***

# 14. Database Migration SQL

Sekarang isi semua file migration.

***

## 14.1 `001_create_extensions.sql`

```sql
CREATE EXTENSION IF NOT EXISTS "pgcrypto";
```

***

## 14.2 `002_create_users.sql`

```sql
CREATE TABLE IF NOT EXISTS users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    username VARCHAR(100) NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    display_name VARCHAR(150) NOT NULL,
    role VARCHAR(50) NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_users_role
        CHECK (role IN ('Customer', 'Admin', 'Ops'))
);
```

***

## 14.3 `003_create_products.sql`

```sql
CREATE TABLE IF NOT EXISTS products (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    sku VARCHAR(100) NOT NULL UNIQUE,
    name VARCHAR(200) NOT NULL,
    stock_quantity INT NOT NULL,
    price NUMERIC(18, 2) NOT NULL,
    row_version BIGINT NOT NULL DEFAULT 1,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_products_stock_non_negative
        CHECK (stock_quantity >= 0),

    CONSTRAINT chk_products_price_non_negative
        CHECK (price >= 0)
);
```

***

## 14.4 `004_create_orders.sql`

```sql
CREATE TABLE IF NOT EXISTS orders (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    order_number VARCHAR(50) NOT NULL UNIQUE,
    customer_id UUID NOT NULL REFERENCES users(id),
    status VARCHAR(50) NOT NULL,
    shipping_address TEXT NOT NULL,
    total_amount NUMERIC(18, 2) NOT NULL,
    row_version BIGINT NOT NULL DEFAULT 1,
    created_by UUID NOT NULL REFERENCES users(id),
    updated_by UUID NULL REFERENCES users(id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_orders_status
        CHECK (status IN ('Pending', 'Confirmed', 'Shipped', 'Delivered', 'Cancelled')),

    CONSTRAINT chk_orders_total_amount_non_negative
        CHECK (total_amount >= 0)
);
```

***

## 14.5 `005_create_order_items.sql`

```sql
CREATE TABLE IF NOT EXISTS order_items (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    order_id UUID NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    product_id UUID NOT NULL REFERENCES products(id),
    product_name_snapshot VARCHAR(200) NOT NULL,
    unit_price_snapshot NUMERIC(18, 2) NOT NULL,
    quantity INT NOT NULL,
    line_total NUMERIC(18, 2) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_order_items_quantity_positive
        CHECK (quantity > 0),

    CONSTRAINT chk_order_items_unit_price_non_negative
        CHECK (unit_price_snapshot >= 0),

    CONSTRAINT chk_order_items_line_total_non_negative
        CHECK (line_total >= 0)
);
```

***

## 14.6 `006_create_inventory_movements.sql`

```sql
CREATE TABLE IF NOT EXISTS inventory_movements (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    product_id UUID NOT NULL REFERENCES products(id),
    order_id UUID NULL REFERENCES orders(id),
    movement_type VARCHAR(50) NOT NULL,
    quantity INT NOT NULL,
    stock_before INT NOT NULL,
    stock_after INT NOT NULL,
    reason TEXT NULL,
    created_by UUID NULL REFERENCES users(id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_inventory_movements_type
        CHECK (movement_type IN ('OrderCreatedDeduction', 'OrderCancelledRestore', 'ManualAdjustment')),

    CONSTRAINT chk_inventory_movements_quantity_positive
        CHECK (quantity > 0),

    CONSTRAINT chk_inventory_movements_stock_non_negative
        CHECK (stock_before >= 0 AND stock_after >= 0)
);
```

***

## 14.7 `007_create_order_status_history.sql`

```sql
CREATE TABLE IF NOT EXISTS order_status_history (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    order_id UUID NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    from_status VARCHAR(50) NULL,
    to_status VARCHAR(50) NOT NULL,
    reason TEXT NULL,
    changed_by UUID NOT NULL REFERENCES users(id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_order_status_history_from_status
        CHECK (
            from_status IS NULL OR
            from_status IN ('Pending', 'Confirmed', 'Shipped', 'Delivered', 'Cancelled')
        ),

    CONSTRAINT chk_order_status_history_to_status
        CHECK (to_status IN ('Pending', 'Confirmed', 'Shipped', 'Delivered', 'Cancelled'))
);
```

***

## 14.8 `008_create_idempotency_keys.sql`

```sql
CREATE TABLE IF NOT EXISTS idempotency_keys (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    key VARCHAR(200) NOT NULL,
    user_id UUID NOT NULL REFERENCES users(id),
    endpoint VARCHAR(200) NOT NULL,
    request_hash TEXT NOT NULL,
    status VARCHAR(50) NOT NULL,
    response_status_code INT NULL,
    response_body JSONB NULL,
    resource_type VARCHAR(100) NULL,
    resource_id UUID NULL,
    locked_until TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_idempotency_keys_status
        CHECK (status IN ('InProgress', 'Completed', 'Failed')),

    CONSTRAINT uq_idempotency_user_key_endpoint
        UNIQUE (user_id, key, endpoint)
);
```

***

## 14.9 `009_create_payments.sql`

```sql
CREATE TABLE IF NOT EXISTS payments (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    order_id UUID NOT NULL REFERENCES orders(id),
    amount NUMERIC(18, 2) NOT NULL,
    status VARCHAR(50) NOT NULL,
    provider VARCHAR(100) NOT NULL,
    payment_reference VARCHAR(200) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_payments_status
        CHECK (status IN ('Pending', 'Paid', 'Failed', 'Cancelled', 'RefundRequired', 'Refunded')),

    CONSTRAINT chk_payments_amount_non_negative
        CHECK (amount >= 0)
);
```

***

## 14.10 `010_create_indexes.sql`

```sql
CREATE INDEX IF NOT EXISTS idx_products_is_active
ON products(is_active);

CREATE INDEX IF NOT EXISTS idx_orders_customer_id
ON orders(customer_id);

CREATE INDEX IF NOT EXISTS idx_orders_status
ON orders(status);

CREATE INDEX IF NOT EXISTS idx_orders_created_at
ON orders(created_at);

CREATE INDEX IF NOT EXISTS idx_orders_customer_status_created
ON orders(customer_id, status, created_at);

CREATE INDEX IF NOT EXISTS idx_order_items_order_id
ON order_items(order_id);

CREATE INDEX IF NOT EXISTS idx_order_items_product_id
ON order_items(product_id);

CREATE INDEX IF NOT EXISTS idx_inventory_movements_product_id
ON inventory_movements(product_id);

CREATE INDEX IF NOT EXISTS idx_inventory_movements_order_id
ON inventory_movements(order_id);

CREATE INDEX IF NOT EXISTS idx_inventory_movements_created_at
ON inventory_movements(created_at);

CREATE INDEX IF NOT EXISTS idx_order_status_history_order_id
ON order_status_history(order_id);

CREATE INDEX IF NOT EXISTS idx_idempotency_keys_user_endpoint
ON idempotency_keys(user_id, endpoint);

CREATE INDEX IF NOT EXISTS idx_idempotency_keys_status
ON idempotency_keys(status);

CREATE INDEX IF NOT EXISTS idx_idempotency_keys_created_at
ON idempotency_keys(created_at);

CREATE INDEX IF NOT EXISTS idx_payments_order_id
ON payments(order_id);

CREATE INDEX IF NOT EXISTS idx_payments_status
ON payments(status);

CREATE UNIQUE INDEX IF NOT EXISTS uq_payments_one_paid_per_order
ON payments(order_id)
WHERE status = 'Paid';
```

***

# 15. Seed SQL untuk Nanti

Ini belum otomatis dieksekusi migration runner. Tapi isi file seed-nya dulu biar siap.

## 15.1 `db/seed/001_seed_users.sql`

```sql
INSERT INTO users (username, password_hash, display_name, role, is_active)
VALUES
    ('admin', crypt('Password123!', gen_salt('bf', 10)), 'System Admin', 'Admin', TRUE),
    ('ops', crypt('Password123!', gen_salt('bf', 10)), 'Operations User', 'Ops', TRUE),
    ('customer1', crypt('Password123!', gen_salt('bf', 10)), 'Customer One', 'Customer', TRUE),
    ('customer2', crypt('Password123!', gen_salt('bf', 10)), 'Customer Two', 'Customer', TRUE)
ON CONFLICT (username) DO NOTHING;
```

***

## 15.2 `db/seed/002_seed_products.sql`

```sql
INSERT INTO products (sku, name, stock_quantity, price, is_active)
VALUES
    ('PRD-MOUSE-001', 'Mouse Wireless', 15, 150000, TRUE),
    ('PRD-KEYBOARD-001', 'Mechanical Keyboard', 20, 450000, TRUE),
    ('PRD-HEADSET-001', 'Gaming Headset', 10, 350000, TRUE)
ON CONFLICT (sku) DO NOTHING;
```

***

# 16. Cara Run Batch 1

Pastikan PostgreSQL sudah jalan dan database sudah ada:

```bash
createdb order_management
```

Kalau pakai user custom, pastikan user/database sesuai connection string:

```text
Host=localhost
Port=5432
Database=order_management
Username=order_user
Password=order_password
```

Lalu run:

```bash
dotnet build
```

```bash
dotnet run --project src/OrderManagement.Api/OrderManagement.Api.csproj
```

Saat API startup, dia akan:

```text
1. Baca folder db/migrations
2. Create schema_migrations jika belum ada
3. Apply migration berurutan
4. Simpan checksum migration
5. Skip migration yang sudah pernah sukses
6. Error jika migration yang sudah applied diubah isinya
```

Ini penting bro, karena dengan checksum kita bisa bilang:

> “Aplikasi melakukan schema verification saat startup. Applied migration tidak boleh diubah diam-diam; harus create migration baru.”

***

# 17. Manual Run Seed

Setelah API sukses apply schema, lu bisa seed manual:

```bash
psql "Host=localhost Port=5432 Dbname=order_management User=order_user Password=order_password" -f db/seed/001_seed_users.sql
psql "Host=localhost Port=5432 Dbname=order_management User=order_user Password=order_password" -f db/seed/002_seed_products.sql
```

Kalau format connection string `psql` di environment lu rewel, bisa pakai:

```bash
PGPASSWORD=order_password psql -h localhost -p 5432 -U order_user -d order_management -f db/seed/001_seed_users.sql
PGPASSWORD=order_password psql -h localhost -p 5432 -U order_user -d order_management -f db/seed/002_seed_products.sql
```

***

# 18. Validasi Setelah Run

Cek table:

```bash
PGPASSWORD=order_password psql -h localhost -p 5432 -U order_user -d order_management -c "\dt"
```

Cek migration:

```bash
PGPASSWORD=order_password psql -h localhost -p 5432 -U order_user -d order_management -c "SELECT * FROM schema_migrations ORDER BY migration_name;"
```

Cek products setelah seed:

```bash
PGPASSWORD=order_password psql -h localhost -p 5432 -U order_user -d order_management -c "SELECT sku, name, stock_quantity, price FROM products;"
```

***

# 19. Commit untuk Batch 1

Saran commit:

```bash
git add .
git commit -m "feat: add infrastructure config and database migration runner"
```

***

# 20. Next Batch

Setelah Batch 1 ini compile dan API startup bisa apply schema, kita lanjut ke:

```text
Batch 2: Domain Model DDD, Enums, Value Objects, Error Codes, Exceptions
```

Di Batch 2 kita akan isi:

```text
Domain/Common/Entity.cs
Domain/Common/AuditableEntity.cs
Domain/Enums/*
Domain/ValueObjects/Money.cs
Domain/Entities/*
Domain/Rules/Facts/*
Domain/Rules/Results/*
Application/Constants/ErrorCodes.cs
Application/Exceptions/*
```

Dari situ fondasi DDD-nya mulai kebentuk, baru lanjut global exception dan auth.
