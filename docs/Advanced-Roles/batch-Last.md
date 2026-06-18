kita lanjut **Batch 16F — Final Role Refactor Stabilization**. Ini batch wajib karena role refactor dari:

```text
Customer / Admin / Ops
```

ke:

```text
Buyer / SellerAdmin / SellerOperator / ApplicationAdmin / DevOps
```

itu impact-nya besar ke code, tests, seed, docs, dan flow authorization.

Batch ini fokus memastikan:

```text
- Tidak ada old role tersisa.
- Tests seed data sudah role final.
- Product test data sudah punya store_id.
- Order create test masih valid karena order sekarang wajib single-store.
- Backoffice/internal logs policy sesuai role final.
- README dan api-contract sudah tidak misleading.
- Build/test bisa jalan.
```

***

# Batch 16F — Final Role Refactor Stabilization

***

## 1. Grep Old Roles

Run dari root repository:

```bash
grep -R "UserRole.Customer\|UserRole.Admin\|UserRole.Ops" -n src tests || true
grep -R "'Customer'\|'Admin'\|'Ops'\|\"Customer\"\|\"Admin\"\|\"Ops\"" -n src tests db docs postman || true
grep -R "customer1\|customer2\|admin\|ops" -n tests docs postman scripts README.md || true
```

Target akhir:

```text
Tidak ada referensi enum lama:
- UserRole.Customer
- UserRole.Admin
- UserRole.Ops

Tidak ada role string lama:
- Customer
- Admin
- Ops
```

Catatan:

```text
Username legacy seperti customer1/admin/ops boleh tetap ada hanya kalau sengaja untuk backward demo.
Tapi untuk final POC lebih clean pakai buyer1/appadmin/devops/selleradmin1.
```

***

# 2. Final Role Mapping

Gunakan mapping berikut di semua code/docs/tests:

```text
Old Customer -> Buyer
Old Admin    -> ApplicationAdmin
Old Ops      -> DevOps, kalau konteks observability/logging
Old Ops      -> SellerOperator, kalau konteks operasional toko
```

Final role enum:

```csharp
public enum UserRole
{
    Buyer = 1,
    SellerAdmin = 2,
    SellerOperator = 3,
    ApplicationAdmin = 4,
    DevOps = 5
}
```

***

# 3. Stabilize NRules Role Logic

File biasanya:

```text
src/OrderManagement.Infrastructure/Rules/NRulesOrderRulesService.cs
```

Pastikan role logic final seperti ini.

## 3.1 Helper Role Methods

Tambahkan private helpers:

```csharp
private static bool IsBuyerLike(UserRole role)
{
    return role is UserRole.Buyer or UserRole.SellerAdmin;
}

private static bool IsSellerBackoffice(UserRole role)
{
    return role is UserRole.SellerAdmin or UserRole.SellerOperator;
}

private static bool IsBusinessAdmin(UserRole role)
{
    return role == UserRole.ApplicationAdmin;
}

private static bool CanMutateBusinessOrder(UserRole role)
{
    return role is UserRole.SellerAdmin
        or UserRole.SellerOperator
        or UserRole.ApplicationAdmin;
}

private static bool CanCancelOrder(UserRole role)
{
    return role is UserRole.Buyer
        or UserRole.SellerAdmin
        or UserRole.SellerOperator
        or UserRole.ApplicationAdmin;
}

private static bool CanPayOrder(UserRole role)
{
    return role is UserRole.Buyer
        or UserRole.SellerAdmin
        or UserRole.ApplicationAdmin;
}
```

## 3.2 Payment Rule

Pastikan payment reject untuk:

```text
SellerOperator
DevOps
```

Logic minimal:

```csharp
if (!CanPayOrder(fact.RequestedByRole))
{
    return RuleResult.Rejected(
        ErrorCodes.PaymentNotAllowed,
        "User role is not allowed to create payment.");
}

if (fact.CurrentOrderStatus != OrderStatus.Pending)
{
    return RuleResult.Rejected(
        ErrorCodes.PaymentNotAllowed,
        "Payment is only allowed when order status is Pending.");
}

if (fact.HasExistingPaidPayment)
{
    return RuleResult.Rejected(
        ErrorCodes.PaymentAlreadyPaid,
        "Order already has a paid payment.");
}

return RuleResult.Allowed();
```

## 3.3 Status Transition Rule

Allowed actor for business mutation:

```text
SellerAdmin
SellerOperator
ApplicationAdmin
```

Reject:

```text
Buyer
DevOps
```

```csharp
if (!CanMutateBusinessOrder(fact.RequestedByRole))
{
    return RuleResult.Rejected(
        ErrorCodes.InvalidOrderStatusTransition,
        "User role is not allowed to update order status.");
}
```

Lalu lifecycle tetap:

```text
Pending -> Confirmed
Confirmed -> Shipped
Shipped -> Delivered
```

Dan `Cancelled` tetap harus via cancel endpoint.

## 3.4 Cancel Rule

Allowed actor:

```text
Buyer
SellerAdmin
SellerOperator
ApplicationAdmin
```

Reject:

```text
DevOps
```

```csharp
if (!CanCancelOrder(fact.RequestedByRole))
{
    return RuleResult.Rejected(
        ErrorCodes.InvalidOrderStatusTransition,
        "User role is not allowed to cancel order.");
}
```

Cancel valid status:

```text
Pending
Confirmed
```

***

# 4. Stabilize OrderService Role Checks

File:

```text
src/OrderManagement.Application/Services/OrderService.cs
```

Pastikan public buyer endpoint logic final begini.

## 4.1 Create Order

```csharp
if (currentRole == UserRole.SellerOperator)
{
    throw new ForbiddenAppException("Seller operator cannot create buyer order.");
}

if (currentRole == UserRole.DevOps)
{
    throw new ForbiddenAppException("DevOps cannot create order.");
}

if (currentRole is UserRole.Buyer or UserRole.SellerAdmin &&
    command.CustomerId != currentUserId)
{
    throw new ForbiddenAppException("Buyer can only create order for themselves.");
}
```

## 4.2 List Buyer Orders

```csharp
var securedQuery = currentRole is UserRole.Buyer or UserRole.SellerAdmin
    ? new ListOrdersQueryDto
    {
        CustomerId = currentUserId,
        Page = normalizedQuery.Page,
        PageSize = normalizedQuery.PageSize,
        Status = normalizedQuery.Status,
        FromDate = normalizedQuery.FromDate,
        ToDate = normalizedQuery.ToDate
    }
    : currentRole == UserRole.ApplicationAdmin
        ? normalizedQuery
        : throw new ForbiddenAppException("You do not have permission to list buyer orders.");
```

## 4.3 Get Buyer Order Detail

```csharp
private void EnsureCanAccessOrder(Guid customerId)
{
    if (_currentUserContext.Role == UserRole.ApplicationAdmin)
    {
        return;
    }

    if (_currentUserContext.Role is UserRole.Buyer or UserRole.SellerAdmin &&
        _currentUserContext.UserId == customerId)
    {
        return;
    }

    throw new ForbiddenAppException("You do not have permission to access this order.");
}
```

## 4.4 Public Update Status

Public endpoint lama:

```http
PATCH /api/v1/orders/{id}/status
```

Harus ApplicationAdmin only:

```csharp
if (currentRole != UserRole.ApplicationAdmin)
{
    throw new ForbiddenAppException(
        "Only Application Admin can update order status from this endpoint. Seller users must use backoffice order endpoint.");
}
```

Seller update status pakai:

```http
PATCH /api/v1/backoffice/orders/{id}/status
```

***

# 5. Stabilize Payment Role Checks

File:

```text
src/OrderManagement.Infrastructure/Repositories/PaymentRepository.cs
```

Pastikan logic final:

```csharp
if (request.RequestedByRole is UserRole.Buyer or UserRole.SellerAdmin &&
    order.CustomerId != request.RequestedBy)
{
    throw new ForbiddenAppException("Buyer can only pay their own order.");
}

if (request.RequestedByRole == UserRole.SellerOperator)
{
    throw new ForbiddenAppException("Seller operator cannot create buyer payment.");
}

if (request.RequestedByRole == UserRole.DevOps)
{
    throw new ForbiddenAppException("DevOps cannot create payment.");
}

if (request.RequestedByRole is not (
        UserRole.Buyer or
        UserRole.SellerAdmin or
        UserRole.ApplicationAdmin))
{
    throw new ForbiddenAppException("User is not allowed to create payment.");
}
```

***

# 6. Stabilize Cancel Role Checks

File:

```text
src/OrderManagement.Infrastructure/Repositories/OrderRepository.cs
```

Dalam `CancelAsync`, pastikan:

```csharp
if (request.CancelledByRole is UserRole.Buyer or UserRole.SellerAdmin &&
    order.CustomerId != request.CancelledBy)
{
    throw new ForbiddenAppException("Buyer can only cancel their own order.");
}

if (request.CancelledByRole is not (
        UserRole.Buyer or
        UserRole.SellerAdmin or
        UserRole.SellerOperator or
        UserRole.ApplicationAdmin))
{
    throw new ForbiddenAppException("User is not allowed to cancel order.");
}
```

DevOps tidak boleh.

***

# 7. Stabilize OrderCancellationPolicy

File:

```text
src/OrderManagement.Application/Services/OrderCancellationPolicy.cs
```

Pastikan final:

```csharp
private static bool IsBuyerLike(UserRole role)
{
    return role is UserRole.Buyer or UserRole.SellerAdmin;
}
```

Resolve default reason:

```csharp
if (string.IsNullOrWhiteSpace(reason))
{
    return IsBuyerLike(currentRole)
        ? OrderCancellationReason.CustomerRequested
        : OrderCancellationReason.OperationalIssue;
}
```

Restriction:

```csharp
if (IsBuyerLike(currentRole) &&
    parsed != OrderCancellationReason.CustomerRequested)
{
    throw new ForbiddenAppException("Buyer can only cancel with CustomerRequested reason.");
}
```

Tapi ada nuance penting:

```text
SellerAdmin bisa acting as Buyer di public buyer endpoint.
SellerAdmin bisa acting as Seller di backoffice endpoint.
```

Untuk Batch 16F, policy ini masih global. Kalau SellerAdmin backoffice ingin cancel dengan `StockUnavailable`, policy di atas akan melarang karena SellerAdmin dianggap buyer-like.

## Fix Recommended

Update policy supaya SellerAdmin tidak dipaksa CustomerRequested saat backoffice.

Tambah optional parameter? Karena existing interface belum punya source. Lebih clean:

### Update Interface

File:

```text
src/OrderManagement.Application/Abstractions/Orders/IOrderCancellationPolicy.cs
```

Ganti method:

```csharp
OrderCancellationDecision Resolve(
    string? cancellationReason,
    string? freeTextReason,
    UserRole currentRole,
    bool isBuyerInitiated);
```

### Update Implementation

```csharp
public OrderCancellationDecision Resolve(
    string? cancellationReason,
    string? freeTextReason,
    UserRole currentRole,
    bool isBuyerInitiated)
{
    var resolvedReason = ResolveCancellationReason(
        cancellationReason,
        currentRole,
        isBuyerInitiated);

    var restoreStock = ShouldRestoreStock(resolvedReason);

    return new OrderCancellationDecision
    {
        CancellationReason = resolvedReason,
        RestoreStock = restoreStock,
        ReasonText = BuildReasonText(freeTextReason, resolvedReason, restoreStock)
    };
}

private static OrderCancellationReason ResolveCancellationReason(
    string? reason,
    UserRole currentRole,
    bool isBuyerInitiated)
{
    if (string.IsNullOrWhiteSpace(reason))
    {
        return isBuyerInitiated
            ? OrderCancellationReason.CustomerRequested
            : OrderCancellationReason.OperationalIssue;
    }

    if (!Enum.TryParse<OrderCancellationReason>(reason, ignoreCase: true, out var parsed))
    {
        throw new BusinessRuleAppException(
            ErrorCodes.InvalidCancellationReason,
            "Cancellation reason is invalid.");
    }

    if (isBuyerInitiated &&
        parsed != OrderCancellationReason.CustomerRequested)
    {
        throw new ForbiddenAppException("Buyer can only cancel with CustomerRequested reason.");
    }

    if (currentRole == UserRole.DevOps)
    {
        throw new ForbiddenAppException("DevOps cannot cancel order.");
    }

    return parsed;
}
```

### Update Public OrderService Cancel

Buyer endpoint:

```csharp
var cancellationDecision = _orderCancellationPolicy.Resolve(
    command.CancellationReason,
    command.Reason,
    currentRole,
    isBuyerInitiated: true);
```

### Update BackofficeOrderService Cancel

Backoffice endpoint:

```csharp
var cancellationDecision = _orderCancellationPolicy.Resolve(
    command.CancellationReason,
    command.Reason,
    currentRole,
    isBuyerInitiated: false);
```

Ini penting bro supaya:

```text
SellerAdmin buyer cancel:
  only CustomerRequested

SellerAdmin backoffice cancel:
  can use StockUnavailable / InventoryMismatch / OperationalIssue
```

***

# 8. Update Authorization Policies Final

File:

```text
src/OrderManagement.Api/Extensions/AuthorizationPolicies.cs
```

Pastikan final constants:

```csharp
public const string AuthenticatedUser = "AuthenticatedUser";

public const string BuyerOnly = "BuyerOnly";
public const string BuyerOrSellerAdmin = "BuyerOrSellerAdmin";

public const string SellerAdminOnly = "SellerAdminOnly";
public const string SellerOperatorOnly = "SellerOperatorOnly";
public const string SellerAdminOrOperator = "SellerAdminOrOperator";

public const string ApplicationAdminOnly = "ApplicationAdminOnly";
public const string DevOpsOnly = "DevOpsOnly";
public const string ApplicationAdminOrDevOps = "ApplicationAdminOrDevOps";

public const string StoreBackofficeUser = "StoreBackofficeUser";
public const string InternalUser = "InternalUser";
```

Internal activity logs API should use:

```csharp
[Authorize(Policy = AuthorizationPolicies.ApplicationAdminOrDevOps)]
```

Not old:

```text
AdminOrOps
```

Backoffice business endpoints should use:

```csharp
[Authorize(Policy = AuthorizationPolicies.StoreBackofficeUser)]
```

***

# 9. Update Internal Activity Logs Access

Files:

```text
src/OrderManagement.Api/Controllers/Internal/InternalActivityLogsController.cs
src/OrderManagement.Api/Controllers/Internal/InternalActivityLogsTestController.cs
```

Change:

```csharp
[Authorize(Policy = AuthorizationPolicies.AdminOrOps)]
```

to:

```csharp
[Authorize(Policy = AuthorizationPolicies.ApplicationAdminOrDevOps)]
```

Because final requirement:

```text
ApplicationAdmin can see all logs.
DevOps can only see logging/observability.
```

***

# 10. Update Tests Seed Roles

File:

```text
tests/OrderManagement.IntegrationTests/Infrastructure/OrderManagementApiFactory.cs
```

Update test users constants.

## 10.1 TestUsers final

```csharp
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
```

## 10.2 TestStores

Add:

```csharp
public static class TestStores
{
    public static readonly Guid SellerStore1Id = Guid.Parse("77777777-7777-7777-7777-777777777777");
}
```

## 10.3 ResetDatabase users insert

Replace users insert with:

```csharp
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
```

## 10.4 Insert store and membership

Add after users insert:

```csharp
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
```

## 10.5 Product seed must include store\_id

Replace products insert:

```csharp
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
```

***

# 11. Update Test Login Usernames

Grep:

```bash
grep -R "\"admin\"\|\"ops\"\|\"customer1\"\|\"customer2\"" -n tests
```

Replace:

```text
admin     -> appadmin
ops       -> devops
customer1 -> buyer1
customer2 -> buyer2
```

Examples:

```csharp
var adminToken = await AuthHelper.LoginAsync(client, "appadmin");
var customerToken = await AuthHelper.LoginAsync(client, "buyer1");
```

Variable names can stay `adminToken` / `customerToken` if lu mau minimize refactor, tapi lebih clean rename later.

***

# 12. Update Integration Test Expectations

## 12.1 Create order tests

Old:

```csharp
TestUsers.Customer1Id
```

Can remain because alias maps to Buyer1Id.

Better:

```csharp
TestUsers.Buyer1Id
```

## 12.2 Admin update status tests

Use:

```csharp
var adminToken = await AuthHelper.LoginAsync(client, "appadmin");
```

## 12.3 DevOps access negative test

If ada internal logs test:

```csharp
var devOpsToken = await AuthHelper.LoginAsync(client, "devops");
```

DevOps should be allowed for logs:

```text
/api/v1/internal/activity-logs -> 200
```

DevOps should be forbidden for business:

```text
/api/v1/backoffice/orders -> 403
/api/v1/backoffice/products -> 403
```

***

# 13. Update Docs: API Contract Role Matrix

File:

```text
docs/api-contract.md
```

Replace role section with:

````markdown
# Authorization Role Matrix

Final roles:

```text
Buyer
SellerAdmin
SellerOperator
ApplicationAdmin
DevOps
````

## Buyer

Can:

```text
- Login
- View active products
- Create order for self
- View own orders
- Cancel own order with CustomerRequested
- Pay own order
```

Cannot:

```text
- Manage products
- Manage store
- Access backoffice dashboard
- Access internal activity logs
```

## SellerAdmin

Can:

```text
- Act as Buyer for own purchases
- Open store
- Manage own store
- Create/manage SellerOperator
- Manage products in own store
- Upload product image in own store
- Adjust stock in own store
- View/update/cancel orders for own store via backoffice
```

Cannot:

```text
- Manage other seller stores
- Access global internal logs unless explicitly allowed
```

## SellerOperator

Can:

```text
- Login
- Operate assigned store
- Manage products in assigned store
- Adjust stock in assigned store
- View/update/cancel orders in assigned store via backoffice
```

Cannot:

```text
- Register as buyer
- Create buyer order
- Pay order as buyer
- Open store
- Create other operators
- Access other stores
- Access internal activity logs
```

## ApplicationAdmin

Can:

```text
- Manage all business data
- Access all stores/products/orders
- Access activity logs
- Create DevOps user in future admin panel
```

## DevOps

Can:

```text
- Access activity logs
- Access observability/health diagnostics
```

Cannot:

```text
- Create/update products
- Adjust stock
- Update/cancel orders
- Create payment
- Manage stores
```

````

---

# 14. Update README Role Explanation

File:

```text
README.md
````

Add/replace section:

````markdown
## User Roles

The application uses ecommerce-oriented roles:

```text
Buyer
SellerAdmin
SellerOperator
ApplicationAdmin
DevOps
````

### Buyer

Pure customer who can browse products, create orders, pay orders, and view/cancel their own orders.

### SellerAdmin

A Buyer who opens a store. SellerAdmin can still act as Buyer for purchases, but also manages their own store, products, store operators, stock, and store orders.

### SellerOperator

Created by SellerAdmin from store panel. SellerOperator cannot register publicly and cannot act as Buyer. They operate only assigned stores.

### ApplicationAdmin

Internal ecommerce application administrator. Can manage global business data and access activity logs.

### DevOps

Observability-only user. Can access logging and diagnostics but cannot mutate business data such as products, orders, payments, or stores.

````

Add endpoint note:

```markdown
## Backoffice APIs

Seller backoffice endpoints:

```text
GET   /api/v1/backoffice/products
POST  /api/v1/backoffice/products
PATCH /api/v1/backoffice/products/{id}
PATCH /api/v1/backoffice/products/{id}/stock
POST  /api/v1/backoffice/products/{id}/image

GET   /api/v1/backoffice/orders
GET   /api/v1/backoffice/orders/{id}
PATCH /api/v1/backoffice/orders/{id}/status
POST  /api/v1/backoffice/orders/{id}/cancel

GET   /api/v1/backoffice/dashboard/summary
````

DevOps cannot access business backoffice APIs.

````

---

# 15. Update Postman Environment

File:

```text
postman/OrderManagement.local.postman_environment.json
````

Add/update:

```json
{
  "key": "appAdminToken",
  "value": "",
  "enabled": true
},
{
  "key": "devOpsToken",
  "value": "",
  "enabled": true
},
{
  "key": "buyerToken",
  "value": "",
  "enabled": true
},
{
  "key": "sellerToken",
  "value": "",
  "enabled": true
},
{
  "key": "sellerOperatorToken",
  "value": "",
  "enabled": true
},
{
  "key": "storeId",
  "value": "",
  "enabled": true
}
```

Postman login requests should use usernames:

```text
appadmin
devops
buyer1
selleradmin1
selleroperator1
```

***

# 16. Update Demo Script

File:

```text
docs/demo-script.md
```

Replace login section:

```bash
APPADMIN_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"appadmin","password":"Password123!"}')

APPADMIN_TOKEN=$(echo "$APPADMIN_LOGIN" | jq -r '.accessToken')
```

```bash
BUYER_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"buyer1","password":"Password123!"}')

BUYER_TOKEN=$(echo "$BUYER_LOGIN" | jq -r '.accessToken')
BUYER_ID=$(echo "$BUYER_LOGIN" | jq -r '.user.id')
```

```bash
SELLER_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"selleradmin1","password":"Password123!"}')

SELLER_TOKEN=$(echo "$SELLER_LOGIN" | jq -r '.accessToken')
```

```bash
DEVOPS_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"devops","password":"Password123!"}')

DEVOPS_TOKEN=$(echo "$DEVOPS_LOGIN" | jq -r '.accessToken')
```

***

# 17. Update Seed Docs

File:

```text
docs/demo-reset.md
```

Replace default users:

```text
appadmin / Password123!
devops / Password123!
selleradmin1 / Password123!
buyer1 / Password123!
buyer2 / Password123!
```

If operator seeded:

```text
selleroperator1 / Password123!
```

***

# 18. Run Build

```bash
dotnet clean
dotnet restore
dotnet build
```

If build fails, inspect first error. Most likely issues:

```text
- old UserRole reference
- missing validator registration
- IOrderCancellationPolicy signature mismatch
- tests still login old usernames
- ProductDto required fields not mapped
- Orders table store_id not selected/inserted somewhere
```

***

# 19. Run Tests

Unit:

```bash
dotnet test tests/OrderManagement.Tests/OrderManagement.Tests.csproj
```

Integration:

```bash
dotnet test tests/OrderManagement.IntegrationTests/OrderManagement.IntegrationTests.csproj
```

All:

```bash
dotnet test
```

If integration fails due Testcontainers:

```bash
docker ps
sudo systemctl start docker
```

***

# 20. Final Grep Validation

Run again:

```bash
grep -R "UserRole.Customer\|UserRole.Admin\|UserRole.Ops" -n src tests || true
grep -R "'Customer'\|'Admin'\|'Ops'\|\"Customer\"\|\"Admin\"\|\"Ops\"" -n src tests db docs postman || true
```

Expected:

```text
No old role references.
```

Allowed exceptions:

```text
Historical docs changelog if intentionally mentioned.
But prefer no old role in final docs.
```

***

# 21. Runtime Smoke Test

Start API:

```bash
dotnet run --project src/OrderManagement.Api/OrderManagement.Api.csproj
```

## 21.1 Buyer can login

```bash
curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"buyer1","password":"Password123!"}' | jq
```

Expected role:

```text
Buyer
```

## 21.2 Seller can access backoffice products

```bash
SELLER_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"selleradmin1","password":"Password123!"}')

SELLER_TOKEN=$(echo "$SELLER_LOGIN" | jq -r '.accessToken')

curl -k -i "https://localhost:7000/api/v1/backoffice/products?page=1&pageSize=20" \
  -H "Authorization: Bearer $SELLER_TOKEN"
```

Expected:

```text
200 OK
```

## 21.3 Buyer cannot access backoffice

```bash
BUYER_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"buyer1","password":"Password123!"}')

BUYER_TOKEN=$(echo "$BUYER_LOGIN" | jq -r '.accessToken')

curl -k -i "https://localhost:7000/api/v1/backoffice/products?page=1&pageSize=20" \
  -H "Authorization: Bearer $BUYER_TOKEN"
```

Expected:

```text
403 Forbidden
```

## 21.4 DevOps can access logs

```bash
DEVOPS_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"devops","password":"Password123!"}')

DEVOPS_TOKEN=$(echo "$DEVOPS_LOGIN" | jq -r '.accessToken')

curl -k -i "https://localhost:7000/api/v1/internal/activity-logs?page=1&pageSize=20" \
  -H "Authorization: Bearer $DEVOPS_TOKEN"
```

Expected:

```text
200 OK
```

## 21.5 DevOps cannot access business endpoint

```bash
curl -k -i "https://localhost:7000/api/v1/backoffice/orders?page=1&pageSize=20" \
  -H "Authorization: Bearer $DEVOPS_TOKEN"
```

Expected:

```text
403 Forbidden
```

***

# 22. Acceptance Criteria

Batch 16F accepted jika:

```text
[ ] No UserRole.Customer/Admin/Ops references.
[ ] Tests seed users use Buyer/SellerAdmin/SellerOperator/ApplicationAdmin/DevOps.
[ ] Test products have store_id.
[ ] Order create works with store-owned products.
[ ] Buyer can create/pay/cancel own order.
[ ] SellerAdmin can manage own store products/orders.
[ ] SellerOperator can manage assigned store products/orders.
[ ] ApplicationAdmin can access all business data.
[ ] DevOps can access logs but not mutate business data.
[ ] README role section updated.
[ ] api-contract role matrix updated.
[ ] dotnet build passes.
[ ] dotnet test passes.
```

***

# 23. Commit

```bash
git add .
git commit -m "chore: stabilize final ecommerce role model"
```

***