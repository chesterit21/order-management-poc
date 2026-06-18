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