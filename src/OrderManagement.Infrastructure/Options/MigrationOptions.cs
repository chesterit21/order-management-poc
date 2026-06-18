namespace OrderManagement.Infrastructure.Options;

public sealed class MigrationOptions
{
    public const string SectionName = "Migration";

    public bool Enabled { get; init; } = true;

    public string Path { get; init; } = "db/migrations";

    public string? SeedPath { get; init; } = "db/seed";

    public string SchemaTable { get; init; } = "schema_migrations";
}