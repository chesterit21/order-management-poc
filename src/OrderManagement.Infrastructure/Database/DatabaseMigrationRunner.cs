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

        // Run seed scripts after migrations
        await ApplySeedDataAsync(connection, cancellationToken);
    }

    private async Task ApplySeedDataAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        var seedPath = _migrationOptions.SeedPath;
        if (string.IsNullOrEmpty(seedPath))
        {
            _logger.LogInformation("Seed path not configured, skipping seed data.");
            return;
        }

        var resolvedSeedPath = ResolveMigrationPath(seedPath);
        _logger.LogInformation("Applying seed data from path {SeedPath}", resolvedSeedPath);

        var seedFiles = Directory
            .GetFiles(resolvedSeedPath, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (seedFiles.Length == 0)
        {
            _logger.LogWarning("No seed files found in path {SeedPath}", resolvedSeedPath);
            return;
        }

        foreach (var file in seedFiles)
        {
            var seedName = Path.GetFileName(file);
            var sql = await File.ReadAllTextAsync(file, cancellationToken);

            // Check if seed was already applied
            var alreadyApplied = await connection.QuerySingleOrDefaultAsync<string>(
                new CommandDefinition(
                    commandText: $"""
                                  SELECT migration_name
                                  FROM {_migrationOptions.SchemaTable}
                                  WHERE migration_name = @MigrationName
                                  """,
                    parameters: new { MigrationName = seedName },
                    cancellationToken: cancellationToken));

            if (alreadyApplied is not null)
            {
                _logger.LogDebug("Skipping already applied seed {SeedName}", seedName);
                continue;
            }

            _logger.LogInformation("Applying seed data {SeedName}", seedName);

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        commandText: sql,
                        transaction: transaction,
                        cancellationToken: cancellationToken));

                // Record seed as applied in schema_migrations table
                var checksum = ComputeSha256(sql);
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
                            MigrationName = seedName,
                            Checksum = checksum
                        },
                        transaction: transaction,
                        cancellationToken: cancellationToken));

                await transaction.CommitAsync(cancellationToken);
                _logger.LogInformation("Seed data {SeedName} applied successfully", seedName);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
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