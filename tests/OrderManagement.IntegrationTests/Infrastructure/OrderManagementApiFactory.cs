using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace OrderManagement.IntegrationTests.Infrastructure;

public sealed class OrderManagementApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("order_management_tests")
        .WithUsername("test_user")
        .WithPassword("test_password")
        .Build();

    private HttpClient? _client;

    public string ConnectionString => _postgres.GetConnectionString();

    public HttpClient Client => _client ??= CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Force API startup so migration runner applies DB schema.
        _ = Client;

        await ResetDatabaseAsync();
    }

    public new async Task DisposeAsync()
    {
        _client?.Dispose();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = ConnectionString,
                ["Migration:Enabled"] = "true",
                ["Migration:Path"] = "db/migrations",
                ["Jwt:Issuer"] = "OrderManagement.Api",
                ["Jwt:Audience"] = "OrderManagement.Clients",
                ["Jwt:Secret"] = "Aday_ Solus_ Teknologi_2026______",
                ["Jwt:AccessTokenExpirationMinutes"] = "60"
            };

            configBuilder.AddInMemoryCollection(overrides);
        });
    }

    public async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync(
            """
            TRUNCATE TABLE
                payments,
                idempotency_keys,
                order_status_history,
                inventory_movements,
                order_items,
                orders,
                products,
                users,
                stores,
                store_members
            RESTART IDENTITY;
            """);

        var appAdminHash = BCrypt.Net.BCrypt.HashPassword("Password123!", 12);
        var devOpsHash = BCrypt.Net.BCrypt.HashPassword("Password123!", 12);
        var buyer1Hash = BCrypt.Net.BCrypt.HashPassword("Password123!", 12);
        var buyer2Hash = BCrypt.Net.BCrypt.HashPassword("Password123!", 12);
        var sellerAdminHash = BCrypt.Net.BCrypt.HashPassword("Password123!", 12);
        var sellerOperatorHash = BCrypt.Net.BCrypt.HashPassword("Password123!", 12);

        await connection.ExecuteAsync(
            """
            INSERT INTO users (id, username, password_hash, display_name, role, is_active)
            VALUES
                (@ApplicationAdminId, 'appadmin', @AppAdminHash, 'Application Admin', 'ApplicationAdmin', TRUE),
                (@DevOpsId, 'devops', @DevOpsHash, 'DevOps User', 'DevOps', TRUE),
                (@Buyer1Id, 'buyer1', @Buyer1Hash, 'Buyer One', 'Buyer', TRUE),
                (@Buyer2Id, 'buyer2', @Buyer2Hash, 'Buyer Two', 'Buyer', TRUE),
                (@SellerAdmin1Id, 'selleradmin1', @SellerAdminHash, 'Seller Admin One', 'SellerAdmin', TRUE),
                (@SellerOperator1Id, 'selleroperator1', @SellerOperatorHash, 'Seller Operator One', 'SellerOperator', TRUE);
            """,
            new
            {
                TestUsers.ApplicationAdminId,
                TestUsers.DevOpsId,
                TestUsers.Buyer1Id,
                TestUsers.Buyer2Id,
                TestUsers.SellerAdmin1Id,
                TestUsers.SellerOperator1Id,
                AppAdminHash = appAdminHash,
                DevOpsHash = devOpsHash,
                Buyer1Hash = buyer1Hash,
                Buyer2Hash = buyer2Hash,
                SellerAdminHash = sellerAdminHash,
                SellerOperatorHash = sellerOperatorHash
            });

        await connection.ExecuteAsync(
            """
            INSERT INTO stores (id, owner_user_id, store_name, slug, description, is_active)
            VALUES
                (@StoreId, @OwnerUserId, 'Seller One Store', 'seller-one-store', 'Integration test seller store.', TRUE);

            INSERT INTO store_members (id, store_id, user_id, role, is_active, created_by)
            VALUES
                (@OwnerMemberId, @StoreId, @OwnerUserId, 'Owner', TRUE, @OwnerUserId),
                (@OperatorMemberId, @StoreId, @OperatorUserId, 'Operator', TRUE, @OwnerUserId);
            """,
            new
            {
                StoreId = TestStores.SellerStore1Id,
                OwnerUserId = TestUsers.SellerAdmin1Id,
                OperatorUserId = TestUsers.SellerOperator1Id,
                OwnerMemberId = Guid.Parse("88888888-8888-8888-8888-888888888881"),
                OperatorMemberId = Guid.Parse("88888888-8888-8888-8888-888888888882")
            });

        await connection.ExecuteAsync(
            """
            INSERT INTO products (id, store_id, sku, name, description, primary_image_url, stock_quantity, price, is_active)
            VALUES
                (@MouseId, @StoreId, 'PRD-MOUSE-001', 'Mouse Wireless', 'Wireless mouse for productivity.', '/uploads/products/placeholder-mouse.webp', 15, 150000, TRUE),
                (@KeyboardId, @StoreId, 'PRD-KEYBOARD-001', 'Mechanical Keyboard', 'Mechanical keyboard for work and gaming.', '/uploads/products/placeholder-keyboard.webp', 20, 450000, TRUE),
                (@HeadsetId, @StoreId, 'PRD-HEADSET-001', 'Gaming Headset', 'Gaming headset with microphone.', '/uploads/products/placeholder-headset.webp', 10, 350000, TRUE);
            """,
            new
            {
                MouseId = TestProducts.MouseId,
                KeyboardId = TestProducts.KeyboardId,
                HeadsetId = TestProducts.HeadsetId,
                StoreId = TestStores.SellerStore1Id
            });
    }
}

public static class TestUsers
{
    public static readonly Guid ApplicationAdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid DevOpsId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Buyer1Id = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid Buyer2Id = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid SellerAdmin1Id = Guid.Parse("55555555-5555-5555-5555-555555555555");
    public static readonly Guid SellerOperator1Id = Guid.Parse("66666666-6666-6666-6666-666666666666");

    // Compatibility aliases to reduce test refactor blast radius.
    public static readonly Guid AdminId = ApplicationAdminId;
    public static readonly Guid OpsId = DevOpsId;
    public static readonly Guid Customer1Id = Buyer1Id;
    public static readonly Guid Customer2Id = Buyer2Id;
}

public static class TestStores
{
    public static readonly Guid SellerStore1Id = Guid.Parse("77777777-7777-7777-7777-777777777777");
}

public static class TestProducts
{
    public static readonly Guid MouseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid KeyboardId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public static readonly Guid HeadsetId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
}