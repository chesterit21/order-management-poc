Wkwkwk iya bro, ini **jebakan Batman beneran** 😄. Dan lu tepat banget baru sadar pas mau masuk ClientSide. Kalau kita lanjut Blazor tanpa beresin konsep **store/seller/operator/product ownership**, nanti UI-nya mentok karena data model backend-nya belum mewakili proses ecommerce yang sebenarnya.

Dan yes, dari requirement terbaru lu, model role sebelumnya harus **direvisi total**, bukan sekadar tambah role `Seller`.

***

# Revisi Konsep Role Final

Kita ubah dari role lama:

```text
Customer
Admin
Ops
Seller optional
```

Menjadi role final sesuai arahan lu:

```text
Buyer
SellerAdmin
SellerOperator
ApplicationAdmin
DevOps
```

Atau kalau mau lebih rapi naming code C#:

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

Mapping bahasa bisnis:

```text
Buyer:
  User pembeli murni.

SellerAdmin:
  Buyer yang membuka toko.
  Dia tetap bisa menjadi pembeli, sekaligus admin toko miliknya.

SellerOperator:
  User yang dibuat oleh SellerAdmin dari panel toko.
  Tidak register dari aplikasi publik.
  Tidak bisa menjadi pembeli.
  Hanya bisa operate toko yang diberikan akses.

ApplicationAdmin:
  Internal admin aplikasi ecommerce.
  Bisa manage user internal, DevOps, audit, moderation, dan observability global.

DevOps:
  Internal technical user.
  Hanya bisa melihat logging, observability, health, trace, activity logs.
  Tidak boleh manage order/product/toko.
```

***

# Konsep Penting: Role Saja Tidak Cukup

Yang paling penting bro: dengan konsep ini kita butuh entity baru:

```text
stores
store_members
```

Karena `SellerAdmin` dan `SellerOperator` bukan cuma role global. Mereka harus punya konteks toko.

Contoh:

```text
Cecep punya Toko A sebagai SellerAdmin.
Budi adalah SellerOperator di Toko A.
Budi tidak boleh akses Toko B.
ApplicationAdmin boleh lihat semua.
DevOps boleh lihat log semua, tapi tidak boleh edit data bisnis.
```

Jadi authorization tidak cukup:

```csharp
RequireRole("SellerOperator")
```

Tapi harus juga cek:

```text
Apakah user ini member dari store yang punya product/order tersebut?
```

***

# Revised Domain Model Tambahan

## 1. `stores`

```text
id
owner_user_id
store_name
slug
description
logo_url
is_active
created_at
updated_at
```

Meaning:

```text
owner_user_id:
  SellerAdmin utama.

store_name:
  Nama toko.

slug:
  URL-friendly unique name.

logo_url:
  Logo toko optional.

is_active:
  Toko aktif/tidak.
```

***

## 2. `store_members`

```text
id
store_id
user_id
role
is_active
created_by
created_at
updated_at
```

Role member di toko:

```text
Owner
Operator
```

Atau langsung pakai:

```text
SellerAdmin
SellerOperator
```

Gue prefer store role dipisah:

```csharp
public enum StoreMemberRole
{
    Owner = 1,
    Operator = 2
}
```

Kenapa?

Karena `UserRole.SellerAdmin` adalah global capability, sedangkan `StoreMemberRole.Owner` adalah hubungan user dengan toko tertentu.

***

## 3. Update `products`

Produk harus punya:

```text
store_id
description
primary_image_url
```

Product belongs to Store:

```text
stores 1 -> many products
```

Bukan cuma seller\_id. Kenapa?

Karena satu SellerAdmin bisa punya lebih dari satu toko di masa depan. Walaupun POC awal satu toko saja, `store_id` lebih benar daripada `seller_id`.

***

# Role Access Matrix Final

## Buyer

Bisa:

```text
- Login
- Lihat produk aktif
- Lihat detail produk
- Create order untuk dirinya
- Lihat order sendiri
- Cancel order sendiri dengan CustomerRequested
- Payment order sendiri
```

Tidak bisa:

```text
- Create/update product
- Upload image product
- Manage store
- Lihat internal logs
```

***

## SellerAdmin

Bisa:

```text
- Semua capability Buyer
- Membuka toko
- Manage toko miliknya
- Create/update product di toko miliknya
- Upload gambar produk di toko miliknya
- Activate/deactivate product di toko miliknya
- Manual stock adjustment produk toko miliknya
- Membuat SellerOperator untuk toko miliknya
- Melihat order yang masuk ke toko miliknya
```

Tidak bisa:

```text
- Lihat toko seller lain
- Manage internal application users
- Lihat global logs kecuali log toko sendiri kalau nanti kita buka
```

***

## SellerOperator

Bisa:

```text
- Login
- Operate toko yang ditugaskan
- Manage product sesuai permission toko
- Update stock produk toko
- Lihat order toko
- Update status order toko jika diizinkan
```

Tidak bisa:

```text
- Register sebagai pembeli
- Membeli produk
- Membuka toko
- Membuat operator lain, kecuali kita kasih permission khusus
- Akses toko lain
- Lihat global logs
```

Important:

```text
SellerOperator dibuat dari panel SellerAdmin, bukan public register.
```

***

## ApplicationAdmin

Bisa:

```text
- Manage internal users
- Create DevOps user
- Lihat semua store
- Lihat semua product/order/payment
- Moderasi toko/product
- Lihat semua activity logs
```

Tidak seharusnya:

```text
- Membeli produk sebagai buyer
```

Tapi ini tergantung policy. Untuk POC, kita bisa treat dia internal-only.

***

## DevOps

Bisa:

```text
- Lihat health
- Lihat activity logs
- Lihat observability/tracing
- Lihat technical diagnostics
```

Tidak bisa:

```text
- Create/update product
- Update order status
- Cancel order
- Payment
- Manage store
- Manage user business
```

***

# Product Audit Activity Logs yang Kita Tambahkan

Kita tambahkan ke `ActivityLogTypes`:

```csharp
public const string ProductCreated = "ProductCreated";
public const string ProductUpdated = "ProductUpdated";
public const string ProductImageUploaded = "ProductImageUploaded";
public const string ProductStockAdjusted = "ProductStockAdjusted";
public const string ProductActivated = "ProductActivated";
public const string ProductDeactivated = "ProductDeactivated";

public const string StoreCreated = "StoreCreated";
public const string StoreUpdated = "StoreUpdated";
public const string StoreOperatorCreated = "StoreOperatorCreated";
public const string StoreOperatorDeactivated = "StoreOperatorDeactivated";
```

Activity metadata aman:

```text
storeId
storeName
productId
sku
stockBefore
stockAfter
adjustmentType
imageUrl
actorUserId
actorRole
```

Tidak boleh log:

```text
password operator
raw uploaded file content
JWT
Authorization header
```

***

# Revisi Batch Planning

Menurut gue kita jangan langsung Blazor dulu. Backend perlu 4 batch tambahan agar ClientSide nanti gak bolong.

***

# Batch 16A — Role Model, Store Ownership, Store Membership

Implement:

```text
- Update UserRole enum:
  Buyer
  SellerAdmin
  SellerOperator
  ApplicationAdmin
  DevOps

- Add StoreMemberRole enum:
  Owner
  Operator

- Migration update users role constraint
- Migration create stores
- Migration create store_members
- Update seed users:
  appadmin
  devops
  selleradmin1
  selleroperator1
  buyer/customer1

- Store domain entity
- StoreMember domain entity
- IStoreRepository
- StoreRepository with Dapper
- IStoreAuthorizationService
- StoreAuthorizationService

- Store APIs:
  POST /api/v1/stores/open
  GET /api/v1/stores/my
  GET /api/v1/stores/{id}
  PATCH /api/v1/stores/{id}

- Seller operator APIs:
  GET /api/v1/stores/{storeId}/operators
  POST /api/v1/stores/{storeId}/operators
  PATCH /api/v1/stores/{storeId}/operators/{operatorUserId}/status
```

Rules:

```text
Buyer can open store -> becomes SellerAdmin.
SellerAdmin owns store.
SellerAdmin can create SellerOperator.
SellerOperator cannot be buyer.
ApplicationAdmin can see all stores.
DevOps cannot manage stores.
```

***

# Batch 16B — Product Management by Store/Seller

Implement:

```text
- Migration update products:
  store_id
  description
  primary_image_url

- Update Product entity/DTO:
  StoreId
  Description
  PrimaryImageUrl

- Backoffice product APIs:
  GET /api/v1/backoffice/products
  POST /api/v1/backoffice/products
  GET /api/v1/backoffice/products/{id}
  PATCH /api/v1/backoffice/products/{id}
  PATCH /api/v1/backoffice/products/{id}/status

- Update public products API:
  GET /api/v1/products includes imageUrl, description, storeName
  GET /api/v1/products/{id} includes detail/image/store

- Authorization:
  SellerAdmin sees own store products.
  SellerOperator sees assigned store products.
  ApplicationAdmin sees all.
  DevOps no access to product management.
  Buyer only public product list/detail.
```

Activity logs:

```text
ProductCreated
ProductUpdated
ProductActivated
ProductDeactivated
```

***

# Batch 16C — Product Image Upload

Implement:

```text
- IFileStorageService
- LocalProductImageStorageService
- FileUploadOptions
- Static file serving from wwwroot/uploads
- POST /api/v1/backoffice/products/{id}/image
- Validate file:
  jpg/jpeg/png/webp
  max 2MB or 5MB
  random file name
  no path traversal
- Update products.primary_image_url
- Activity log ProductImageUploaded
```

Response:

```json
{
  "productId": "uuid",
  "imageUrl": "/uploads/products/{productId}/abc123.webp"
}
```

Production note:

```text
Use blob/object storage in real production.
```

***

# Batch 16D — Manual Stock Adjustment & Seller Dashboard Summary

Implement:

```text
- PATCH /api/v1/backoffice/products/{id}/stock
- adjustmentType:
  Increase
  Decrease
  Set

- Product row lock FOR UPDATE
- Validate stock never negative
- Insert inventory_movements ManualAdjustment
- Activity log ProductStockAdjusted

- Dashboard summary:
  GET /api/v1/backoffice/dashboard/summary
```

Summary response:

```json
{
  "storeId": "uuid",
  "storeName": "Cecep Store",
  "totalProducts": 20,
  "activeProducts": 18,
  "lowStockProducts": 3,
  "pendingOrders": 5,
  "todayOrders": 12,
  "todayRevenue": 1500000
}
```

Access:

```text
SellerAdmin/SellerOperator:
  own store dashboard.

ApplicationAdmin:
  optional global dashboard.

DevOps:
  no business dashboard, only observability dashboard.
```

***

# Batch 17 — Blazor ClientSide Bisa Masuk

Setelah Batch 16A-D, baru masuk Blazor akan smooth.

Blazor screens:

```text
Public:
- Login
- Product catalog with images
- Product detail
- Cart/create order
- My orders
- Payment simulation

Seller Panel:
- My Store dashboard
- Product list
- Create/edit product
- Upload image
- Stock adjustment
- Store orders
- Operator management

Application Admin Panel:
- Store list
- User internal management
- Activity logs

DevOps Panel:
- Activity logs
- Observability page
```

***

# Naming Policy Final

Kita perlu pilih naming internal C#.

Aku sarankan:

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

Dan policy names:

```csharp
BuyerOnly
SellerAdminOnly
SellerOperatorOnly
SellerAdminOrOperator
ApplicationAdminOnly
DevOpsOnly
ApplicationAdminOrDevOps
BackofficeUser
InternalUser
AuthenticatedUser
```

BackofficeUser:

```text
SellerAdmin
SellerOperator
ApplicationAdmin
```

InternalUser:

```text
ApplicationAdmin
DevOps
```

ApplicationAdminOrDevOps:

```text
Can access logs/observability.
```

But internal activity logs maybe:

```text
ApplicationAdminOrDevOps
```

Bukan Admin/Ops lama.

***

# Yang Perlu Kita Refactor dari Implementasi Lama

Karena role lama banyak dipakai:

```text
Customer
Admin
Ops
```

Maka Batch 16A harus refactor:

```text
Customer -> Buyer
Admin -> ApplicationAdmin or SellerAdmin depending context
Ops -> SellerOperator or DevOps depending context
AdminOrOps policy -> harus dipecah
```

Mapping sementara:

```text
Customer -> Buyer
Admin -> ApplicationAdmin
Ops -> DevOps? Tidak tepat.
```

Lebih aman:

```text
Seed ulang role final.
Policy final.
Endpoint access final.
```

Product/order flow:

```text
Buyer order flow:
  tetap sama, hanya rename Customer -> Buyer.

Order update status:
  Sebelumnya Admin/Ops.
  Sekarang harus siapa?
```

Untuk ecommerce seller panel, status order toko sebaiknya:

```text
SellerAdmin/SellerOperator bisa update order toko:
  Pending -> Confirmed? mungkin payment yang confirm.
  Confirmed -> Shipped
  Shipped -> Delivered maybe seller/operator or system.
```

ApplicationAdmin bisa override semua jika perlu.

DevOps tidak boleh.

***

# Revisi Order Access Rule

## Buyer

```text
Create order for self.
Get/list own orders.
Cancel own Pending/Confirmed order with CustomerRequested.
Payment own order.
```

## SellerAdmin/SellerOperator

```text
List orders for own store.
Get order detail if order contains product from own store.
Update shipping status for own store order.
Cancel order for own store with OperationalIssue / StockUnavailable / InventoryMismatch.
```

Tapi ini memunculkan isu multi-store order:

```text
Satu order bisa berisi item dari beberapa toko.
```

Untuk POC, kita bisa simplify:

```text
Satu order hanya boleh berisi produk dari satu store.
```

Ini penting banget bro.

Kalau tidak, order status per seller jadi kompleks karena satu order marketplace biasanya punya shipment per store.

Jadi Batch 16B/B16D harus enforce:

```text
Create order items must belong to same store.
```

Atau future improvement:

```text
Split order into seller sub-orders.
```

Untuk POC, kita pilih:

```text
One order = one store.
```

Maka `orders` perlu column:

```text
store_id
```

Ini penting.

***

# Tambahan Migration Penting

Karena product now belongs to store, order should also belong to store:

```text
orders.store_id UUID NULL/NOT NULL REFERENCES stores(id)
```

Saat create order:

```text
- Load products
- Verify all products same store
- Set orders.store_id
```

Seller access:

```text
SellerAdmin/SellerOperator can access orders where orders.store_id in their store memberships.
```

Ini jauh lebih clean.

***

# Final Revised Batch 16A-D dengan Order Store Ownership

## Batch 16A

```text
Roles + Stores + Store Members
```

## Batch 16B

```text
Products belong to Store + Product Backoffice
```

## Batch 16C

```text
Product Image Upload
```

## Batch 16D

```text
Orders belong to Store + Seller Order Access + Stock Adjustment + Dashboard
```

Kenapa order store ownership di 16D?

Karena create order repository harus berubah untuk set store\_id dan validate same-store cart.

***

# Kesimpulan

Lu bener bro, dan ini bukan minor. Ini adalah domain modeling gap yang besar tapi bagus ketahuan sekarang.

Yang berubah:

```text
Dari simple order management
menjadi mini ecommerce marketplace/store management.
```

Core backend kita tetap berguna:

```text
idempotency
stock lock
payment/cancel race
activity logs
```

Tapi role/access/product/order ownership harus diperluas.

Kalau lu setuju, next aku generate:

```text
Batch 16A: Role Model, Store Ownership, Store Membership
```

Dengan implementasi:

```text
- migration
- enum update
- entity
- DTO
- validator
- repository
- service
- controller
- authorization policies update
- seed update
- activity logs StoreCreated/StoreOperatorCreated
```

Gaskeun bro. Ini **Batch 16A: Role Model, Store Ownership, Store Membership**.

Batch ini akan refactor model dari simple `Customer/Admin/Ops` menjadi ecommerce role yang lebih benar:

```text
Buyer
SellerAdmin
SellerOperator
ApplicationAdmin
DevOps
```

Dan menambahkan foundation:

```text
stores
store_members
store authorization
open store
my stores
store detail/update
operator seller creation/list/deactivate
activity logs StoreCreated/StoreUpdated/StoreOperatorCreated/StoreOperatorDeactivated
```

> Penting bro: Batch 16A ini memang refactor cukup besar karena enum role lama dipakai di banyak flow. Di batch ini kita update code yang terdampak supaya tetap build.

***

# Batch 16A — Role Model, Store Ownership, Store Membership

***

## 0. Role Final

```text
Buyer:
  Pembeli murni.

SellerAdmin:
  Buyer yang membuka toko. Tetap bisa menjadi pembeli.

SellerOperator:
  User yang dibuat dari panel toko oleh SellerAdmin.
  Tidak register publik.
  Tidak bisa menjadi pembeli.

ApplicationAdmin:
  Internal admin aplikasi ecommerce.

DevOps:
  Hanya logging/observability.
```

***

# 1. Migration

Create file:

```text
db/migrations/014_create_stores_and_update_user_roles.sql
```

Isi:

```sql
UPDATE users
SET role = 'Buyer'
WHERE role = 'Customer';

UPDATE users
SET role = 'ApplicationAdmin'
WHERE role = 'Admin';

UPDATE users
SET role = 'DevOps'
WHERE role = 'Ops';

ALTER TABLE users
DROP CONSTRAINT IF EXISTS chk_users_role;

ALTER TABLE users
ADD CONSTRAINT chk_users_role
CHECK (
    role IN (
        'Buyer',
        'SellerAdmin',
        'SellerOperator',
        'ApplicationAdmin',
        'DevOps'
    )
);

CREATE TABLE IF NOT EXISTS stores (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_user_id UUID NOT NULL REFERENCES users(id),
    store_name VARCHAR(150) NOT NULL,
    slug VARCHAR(160) NOT NULL UNIQUE,
    description TEXT NULL,
    logo_url TEXT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_stores_store_name_not_empty
        CHECK (length(trim(store_name)) > 0),

    CONSTRAINT chk_stores_slug_not_empty
        CHECK (length(trim(slug)) > 0)
);

CREATE TABLE IF NOT EXISTS store_members (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    store_id UUID NOT NULL REFERENCES stores(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id),
    role VARCHAR(50) NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_by UUID NOT NULL REFERENCES users(id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_store_members_role
        CHECK (role IN ('Owner', 'Operator')),

    CONSTRAINT uq_store_members_store_user
        UNIQUE (store_id, user_id)
);

CREATE INDEX IF NOT EXISTS idx_stores_owner_user_id
ON stores(owner_user_id);

CREATE INDEX IF NOT EXISTS idx_stores_is_active
ON stores(is_active);

CREATE INDEX IF NOT EXISTS idx_store_members_store_id
ON store_members(store_id);

CREATE INDEX IF NOT EXISTS idx_store_members_user_id
ON store_members(user_id);

CREATE INDEX IF NOT EXISTS idx_store_members_role
ON store_members(role);

CREATE INDEX IF NOT EXISTS idx_store_members_is_active
ON store_members(is_active);
```

***

# 2. Domain Enums

## 2.1 Replace `UserRole.cs`

File:

```text
src/OrderManagement.Domain/Enums/UserRole.cs
```

```csharp
namespace OrderManagement.Domain.Enums;

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

## 2.2 Create `StoreMemberRole.cs`

File:

```text
src/OrderManagement.Domain/Enums/StoreMemberRole.cs
```

```csharp
namespace OrderManagement.Domain.Enums;

public enum StoreMemberRole
{
    Owner = 1,
    Operator = 2
}
```

***

# 3. Domain Constants Update

File:

```text
src/OrderManagement.Domain/Constants/DomainConstants.cs
```

Tambahkan:

```csharp
public const int MaxStoreNameLength = 150;
public const int MaxStoreSlugLength = 160;
public const int MaxStoreDescriptionLength = 1000;
```

Final contoh:

```csharp
namespace OrderManagement.Domain.Constants;

public static class DomainConstants
{
    public const int MaxSkuLength = 100;
    public const int MaxProductNameLength = 200;
    public const int MaxUsernameLength = 100;
    public const int MaxDisplayNameLength = 150;
    public const int MaxOrderNumberLength = 50;
    public const int MaxIdempotencyKeyLength = 200;
    public const int MaxEndpointLength = 200;
    public const int MaxPaymentProviderLength = 100;
    public const int MaxPaymentReferenceLength = 200;

    public const int MaxStoreNameLength = 150;
    public const int MaxStoreSlugLength = 160;
    public const int MaxStoreDescriptionLength = 1000;

    public const string OrderNumberPrefix = "ORD";
}
```

***

# 4. Domain Entities

## 4.1 Create `Store.cs`

File:

```text
src/OrderManagement.Domain/Entities/Store.cs
```

```csharp
using OrderManagement.Domain.Common;

namespace OrderManagement.Domain.Entities;

public sealed class Store : AuditableEntity
{
    private Store()
    {
    }

    private Store(
        Guid id,
        Guid ownerUserId,
        string storeName,
        string slug,
        string? description,
        string? logoUrl,
        bool isActive,
        DateTimeOffset createdAt)
        : base(id)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("Owner user id is required.", nameof(ownerUserId));
        }

        if (string.IsNullOrWhiteSpace(storeName))
        {
            throw new ArgumentException("Store name is required.", nameof(storeName));
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Store slug is required.", nameof(slug));
        }

        OwnerUserId = ownerUserId;
        StoreName = storeName.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        LogoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim();
        IsActive = isActive;
        SetCreatedAt(createdAt);
    }

    public Guid OwnerUserId { get; private set; }

    public string StoreName { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string? LogoUrl { get; private set; }

    public bool IsActive { get; private set; }

    public static Store Create(
        Guid ownerUserId,
        string storeName,
        string slug,
        string? description,
        DateTimeOffset now)
    {
        return new Store(
            Guid.NewGuid(),
            ownerUserId,
            storeName,
            slug,
            description,
            null,
            true,
            now);
    }

    public static Store Rehydrate(
        Guid id,
        Guid ownerUserId,
        string storeName,
        string slug,
        string? description,
        string? logoUrl,
        bool isActive,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        var store = new Store(
            id,
            ownerUserId,
            storeName,
            slug,
            description,
            logoUrl,
            isActive,
            createdAt);

        store.SetUpdatedAt(updatedAt);

        return store;
    }
}
```

***

## 4.2 Create `StoreMember.cs`

File:

```text
src/OrderManagement.Domain/Entities/StoreMember.cs
```

```csharp
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Domain.Entities;

public sealed class StoreMember : AuditableEntity
{
    private StoreMember()
    {
    }

    private StoreMember(
        Guid id,
        Guid storeId,
        Guid userId,
        StoreMemberRole role,
        bool isActive,
        Guid createdBy,
        DateTimeOffset createdAt)
        : base(id)
    {
        if (storeId == Guid.Empty)
        {
            throw new ArgumentException("Store id is required.", nameof(storeId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (createdBy == Guid.Empty)
        {
            throw new ArgumentException("Created by is required.", nameof(createdBy));
        }

        StoreId = storeId;
        UserId = userId;
        Role = role;
        IsActive = isActive;
        CreatedBy = createdBy;
        SetCreatedAt(createdAt);
    }

    public Guid StoreId { get; private set; }

    public Guid UserId { get; private set; }

    public StoreMemberRole Role { get; private set; }

    public bool IsActive { get; private set; }

    public Guid CreatedBy { get; private set; }

    public static StoreMember CreateOwner(
        Guid storeId,
        Guid userId,
        Guid createdBy,
        DateTimeOffset now)
    {
        return new StoreMember(
            Guid.NewGuid(),
            storeId,
            userId,
            StoreMemberRole.Owner,
            true,
            createdBy,
            now);
    }

    public static StoreMember CreateOperator(
        Guid storeId,
        Guid userId,
        Guid createdBy,
        DateTimeOffset now)
    {
        return new StoreMember(
            Guid.NewGuid(),
            storeId,
            userId,
            StoreMemberRole.Operator,
            true,
            createdBy,
            now);
    }

    public static StoreMember Rehydrate(
        Guid id,
        Guid storeId,
        Guid userId,
        StoreMemberRole role,
        bool isActive,
        Guid createdBy,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        var member = new StoreMember(
            id,
            storeId,
            userId,
            role,
            isActive,
            createdBy,
            createdAt);

        member.SetUpdatedAt(updatedAt);

        return member;
    }
}
```

***

# 5. ErrorCodes Update

File:

```text
src/OrderManagement.Application/Constants/ErrorCodes.cs
```

Tambahkan constants:

```csharp
public const string StoreNotFound = "STORE_NOT_FOUND";
public const string StoreAlreadyExists = "STORE_ALREADY_EXISTS";
public const string StoreAccessDenied = "STORE_ACCESS_DENIED";
public const string UserAlreadyExists = "USER_ALREADY_EXISTS";
public const string InvalidStoreRole = "INVALID_STORE_ROLE";
public const string SellerOperatorCannotActAsBuyer = "SELLER_OPERATOR_CANNOT_ACT_AS_BUYER";
```

***

# 6. ActivityLogTypes Update

File:

```text
src/OrderManagement.Application/DTOs/ActivityLogs/ActivityLogTypes.cs
```

Tambahkan:

```csharp
public const string StoreCreated = "StoreCreated";
public const string StoreUpdated = "StoreUpdated";
public const string StoreOperatorCreated = "StoreOperatorCreated";
public const string StoreOperatorDeactivated = "StoreOperatorDeactivated";
```

***

# 7. Current User Context Update

## 7.1 Update Interface

File:

```text
src/OrderManagement.Application/Abstractions/Authentication/ICurrentUserContext.cs
```

Replace:

```csharp
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Abstractions.Authentication;

public interface ICurrentUserContext
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    string? Username { get; }

    string? DisplayName { get; }

    UserRole? Role { get; }

    bool IsInRole(UserRole role);

    bool IsBuyerLike();

    bool IsSellerBackoffice();

    bool IsApplicationAdmin();

    bool IsDevOps();

    bool IsApplicationAdminOrDevOps();

    // Legacy compatibility for previous batches.
    bool IsAdminOrOps();
}
```

***

## 7.2 Update Implementation

File:

```text
src/OrderManagement.Infrastructure/Security/CurrentUserContext.cs
```

Replace:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Infrastructure.Security;

public sealed class CurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public Guid? UserId
    {
        get
        {
            var principal = _httpContextAccessor.HttpContext?.User;

            var value =
                principal?.FindFirstValue(ClaimTypes.NameIdentifier) ??
                principal?.FindFirstValue("sub");

            return Guid.TryParse(value, out var userId)
                ? userId
                : null;
        }
    }

    public string? Username =>
        _httpContextAccessor.HttpContext?.User.FindFirstValue("username") ??
        _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name);

    public string? DisplayName =>
        _httpContextAccessor.HttpContext?.User.FindFirstValue("displayName");

    public UserRole? Role
    {
        get
        {
            var value =
                _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Role) ??
                _httpContextAccessor.HttpContext?.User.FindFirstValue("role");

            return Enum.TryParse<UserRole>(value, ignoreCase: true, out var role)
                ? role
                : null;
        }
    }

    public bool IsInRole(UserRole role)
    {
        return Role == role;
    }

    public bool IsBuyerLike()
    {
        return Role is UserRole.Buyer or UserRole.SellerAdmin;
    }

    public bool IsSellerBackoffice()
    {
        return Role is UserRole.SellerAdmin or UserRole.SellerOperator;
    }

    public bool IsApplicationAdmin()
    {
        return Role == UserRole.ApplicationAdmin;
    }

    public bool IsDevOps()
    {
        return Role == UserRole.DevOps;
    }

    public bool IsApplicationAdminOrDevOps()
    {
        return Role is UserRole.ApplicationAdmin or UserRole.DevOps;
    }

    public bool IsAdminOrOps()
    {
        return IsApplicationAdmin();
    }
}
```

***

# 8. Authorization Policies Update

## 8.1 `AuthorizationPolicies.cs`

File:

```text
src/OrderManagement.Api/Extensions/AuthorizationPolicies.cs
```

Replace:

```csharp
namespace OrderManagement.Api.Extensions;

public static class AuthorizationPolicies
{
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

    // Legacy constants to avoid missing policy name while refactoring older controllers.
    public const string AdminOnly = ApplicationAdminOnly;
    public const string OpsOnly = DevOpsOnly;
    public const string AdminOrOps = ApplicationAdminOnly;
    public const string CustomerOnly = BuyerOnly;
    public const string CustomerOrAdmin = BuyerOrSellerAdmin;
}
```

***

## 8.2 `AuthorizationExtensions.cs`

File:

```text
src/OrderManagement.Api/Extensions/AuthorizationExtensions.cs
```

Replace:

```csharp
using OrderManagement.Domain.Enums;

namespace OrderManagement.Api.Extensions;

public static class AuthorizationExtensions
{
    public static IServiceCollection AddApiAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                AuthorizationPolicies.AuthenticatedUser,
                policy => policy.RequireAuthenticatedUser());

            options.AddPolicy(
                AuthorizationPolicies.BuyerOnly,
                policy => policy.RequireRole(UserRole.Buyer.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.BuyerOrSellerAdmin,
                policy => policy.RequireRole(
                    UserRole.Buyer.ToString(),
                    UserRole.SellerAdmin.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.SellerAdminOnly,
                policy => policy.RequireRole(UserRole.SellerAdmin.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.SellerOperatorOnly,
                policy => policy.RequireRole(UserRole.SellerOperator.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.SellerAdminOrOperator,
                policy => policy.RequireRole(
                    UserRole.SellerAdmin.ToString(),
                    UserRole.SellerOperator.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.ApplicationAdminOnly,
                policy => policy.RequireRole(UserRole.ApplicationAdmin.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.DevOpsOnly,
                policy => policy.RequireRole(UserRole.DevOps.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.ApplicationAdminOrDevOps,
                policy => policy.RequireRole(
                    UserRole.ApplicationAdmin.ToString(),
                    UserRole.DevOps.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.StoreBackofficeUser,
                policy => policy.RequireRole(
                    UserRole.SellerAdmin.ToString(),
                    UserRole.SellerOperator.ToString(),
                    UserRole.ApplicationAdmin.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.InternalUser,
                policy => policy.RequireRole(
                    UserRole.ApplicationAdmin.ToString(),
                    UserRole.DevOps.ToString()));

            // Legacy policy names.
            options.AddPolicy(
                AuthorizationPolicies.AdminOnly,
                policy => policy.RequireRole(UserRole.ApplicationAdmin.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.OpsOnly,
                policy => policy.RequireRole(UserRole.DevOps.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.AdminOrOps,
                policy => policy.RequireRole(UserRole.ApplicationAdmin.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.CustomerOnly,
                policy => policy.RequireRole(UserRole.Buyer.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.CustomerOrAdmin,
                policy => policy.RequireRole(
                    UserRole.Buyer.ToString(),
                    UserRole.SellerAdmin.ToString(),
                    UserRole.ApplicationAdmin.ToString()));
        });

        return services;
    }
}
```

***

# 9. Store DTOs

Buat folder:

```text
src/OrderManagement.Application/DTOs/Stores
```

***

## 9.1 `StoreDto.cs`

```csharp
namespace OrderManagement.Application.DTOs.Stores;

public sealed class StoreDto
{
    public required Guid Id { get; init; }

    public required Guid OwnerUserId { get; init; }

    public required string StoreName { get; init; }

    public required string Slug { get; init; }

    public string? Description { get; init; }

    public string? LogoUrl { get; init; }

    public required bool IsActive { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
```

***

## 9.2 `OpenStoreCommand.cs`

```csharp
namespace OrderManagement.Application.DTOs.Stores;

public sealed class OpenStoreCommand
{
    public required string StoreName { get; init; }

    public string? Description { get; init; }
}
```

***

## 9.3 `UpdateStoreCommand.cs`

```csharp
namespace OrderManagement.Application.DTOs.Stores;

public sealed class UpdateStoreCommand
{
    public required Guid StoreId { get; init; }

    public required string StoreName { get; init; }

    public string? Description { get; init; }
}
```

***

## 9.4 `StoreMemberDto.cs`

```csharp
namespace OrderManagement.Application.DTOs.Stores;

public sealed class StoreMemberDto
{
    public required Guid Id { get; init; }

    public required Guid StoreId { get; init; }

    public required Guid UserId { get; init; }

    public required string Username { get; init; }

    public required string DisplayName { get; init; }

    public required string Role { get; init; }

    public required bool IsActive { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
```

***

## 9.5 `CreateStoreOperatorCommand.cs`

```csharp
namespace OrderManagement.Application.DTOs.Stores;

public sealed class CreateStoreOperatorCommand
{
    public required Guid StoreId { get; init; }

    public required string Username { get; init; }

    public required string Password { get; init; }

    public required string DisplayName { get; init; }
}
```

***

## 9.6 `SetStoreOperatorStatusCommand.cs`

```csharp
namespace OrderManagement.Application.DTOs.Stores;

public sealed class SetStoreOperatorStatusCommand
{
    public required Guid StoreId { get; init; }

    public required Guid OperatorUserId { get; init; }

    public required bool IsActive { get; init; }
}
```

***

## 9.7 `StorePersistenceRequests.cs`

```csharp
namespace OrderManagement.Application.DTOs.Stores;

public sealed class OpenStorePersistenceRequest
{
    public required Guid StoreId { get; init; }

    public required Guid OwnerUserId { get; init; }

    public required string StoreName { get; init; }

    public required string Slug { get; init; }

    public string? Description { get; init; }

    public required DateTimeOffset Now { get; init; }
}

public sealed class UpdateStorePersistenceRequest
{
    public required Guid StoreId { get; init; }

    public required string StoreName { get; init; }

    public string? Description { get; init; }

    public required DateTimeOffset Now { get; init; }
}

public sealed class CreateStoreOperatorPersistenceRequest
{
    public required Guid StoreId { get; init; }

    public required Guid OperatorUserId { get; init; }

    public required string Username { get; init; }

    public required string PasswordHash { get; init; }

    public required string DisplayName { get; init; }

    public required Guid CreatedBy { get; init; }

    public required DateTimeOffset Now { get; init; }
}

public sealed class SetStoreOperatorStatusPersistenceRequest
{
    public required Guid StoreId { get; init; }

    public required Guid OperatorUserId { get; init; }

    public required bool IsActive { get; init; }

    public required DateTimeOffset Now { get; init; }
}
```

***

# 10. Store Abstractions

Buat folder:

```text
src/OrderManagement.Application/Abstractions/Stores
```

***

## 10.1 `IStoreService.cs`

```csharp
using OrderManagement.Application.DTOs.Stores;

namespace OrderManagement.Application.Abstractions.Stores;

public interface IStoreService
{
    Task<StoreDto> OpenStoreAsync(
        OpenStoreCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<StoreDto>> GetMyStoresAsync(
        CancellationToken cancellationToken = default);

    Task<StoreDto> GetByIdAsync(
        Guid storeId,
        CancellationToken cancellationToken = default);

    Task<StoreDto> UpdateAsync(
        UpdateStoreCommand command,
        CancellationToken cancellationToken = default);
}
```

***

## 10.2 `IStoreOperatorService.cs`

```csharp
using OrderManagement.Application.DTOs.Stores;

namespace OrderManagement.Application.Abstractions.Stores;

public interface IStoreOperatorService
{
    Task<IReadOnlyCollection<StoreMemberDto>> ListOperatorsAsync(
        Guid storeId,
        CancellationToken cancellationToken = default);

    Task<StoreMemberDto> CreateOperatorAsync(
        CreateStoreOperatorCommand command,
        CancellationToken cancellationToken = default);

    Task<StoreMemberDto> SetOperatorStatusAsync(
        SetStoreOperatorStatusCommand command,
        CancellationToken cancellationToken = default);
}
```

***

## 10.3 `IStoreAuthorizationService.cs`

```csharp
namespace OrderManagement.Application.Abstractions.Stores;

public interface IStoreAuthorizationService
{
    Task EnsureCanViewStoreAsync(
        Guid storeId,
        CancellationToken cancellationToken = default);

    Task EnsureCanManageStoreAsync(
        Guid storeId,
        CancellationToken cancellationToken = default);

    Task EnsureCanOperateStoreAsync(
        Guid storeId,
        CancellationToken cancellationToken = default);
}
```

***

# 11. Store Repository Abstraction

File:

```text
src/OrderManagement.Application/Abstractions/Repositories/IStoreRepository.cs
```

```csharp
using OrderManagement.Application.DTOs.Stores;

namespace OrderManagement.Application.Abstractions.Repositories;

public interface IStoreRepository
{
    Task<bool> UserHasOwnedStoreAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default);

    Task<StoreDto?> GetByIdAsync(
        Guid storeId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<StoreDto>> ListByUserMembershipAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<StoreDto>> ListAllAsync(
        CancellationToken cancellationToken = default);

    Task<bool> IsStoreOwnerAsync(
        Guid storeId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> IsStoreOperatorAsync(
        Guid storeId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<StoreDto> OpenStoreAsync(
        OpenStorePersistenceRequest request,
        CancellationToken cancellationToken = default);

    Task<StoreDto> UpdateStoreAsync(
        UpdateStorePersistenceRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<StoreMemberDto>> ListOperatorsAsync(
        Guid storeId,
        CancellationToken cancellationToken = default);

    Task<StoreMemberDto> CreateOperatorAsync(
        CreateStoreOperatorPersistenceRequest request,
        CancellationToken cancellationToken = default);

    Task<StoreMemberDto> SetOperatorStatusAsync(
        SetStoreOperatorStatusPersistenceRequest request,
        CancellationToken cancellationToken = default);
}
```

***

# 12. Validators

Buat folder:

```text
src/OrderManagement.Application/Validators/Stores
```

***

## 12.1 `OpenStoreCommandValidator.cs`

```csharp
using FluentValidation;
using OrderManagement.Application.DTOs.Stores;

namespace OrderManagement.Application.Validators.Stores;

public sealed class OpenStoreCommandValidator : AbstractValidator<OpenStoreCommand>
{
    public OpenStoreCommandValidator()
    {
        RuleFor(command => command.StoreName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Store name is required.")
            .MaximumLength(150)
            .WithMessage("Store name cannot be longer than 150 characters.");

        RuleFor(command => command.Description)
            .MaximumLength(1000)
            .WithMessage("Store description cannot be longer than 1000 characters.")
            .When(command => !string.IsNullOrWhiteSpace(command.Description));
    }
}
```

***

## 12.2 `UpdateStoreCommandValidator.cs`

```csharp
using FluentValidation;
using OrderManagement.Application.DTOs.Stores;

namespace OrderManagement.Application.Validators.Stores;

public sealed class UpdateStoreCommandValidator : AbstractValidator<UpdateStoreCommand>
{
    public UpdateStoreCommandValidator()
    {
        RuleFor(command => command.StoreId)
            .NotEmpty()
            .WithMessage("Store id is required.");

        RuleFor(command => command.StoreName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Store name is required.")
            .MaximumLength(150)
            .WithMessage("Store name cannot be longer than 150 characters.");

        RuleFor(command => command.Description)
            .MaximumLength(1000)
            .WithMessage("Store description cannot be longer than 1000 characters.")
            .When(command => !string.IsNullOrWhiteSpace(command.Description));
    }
}
```

***

## 12.3 `CreateStoreOperatorCommandValidator.cs`

```csharp
using FluentValidation;
using OrderManagement.Application.DTOs.Stores;

namespace OrderManagement.Application.Validators.Stores;

public sealed class CreateStoreOperatorCommandValidator : AbstractValidator<CreateStoreOperatorCommand>
{
    public CreateStoreOperatorCommandValidator()
    {
        RuleFor(command => command.StoreId)
            .NotEmpty()
            .WithMessage("Store id is required.");

        RuleFor(command => command.Username)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Username is required.")
            .MaximumLength(100)
            .WithMessage("Username cannot be longer than 100 characters.");

        RuleFor(command => command.DisplayName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Display name is required.")
            .MaximumLength(150)
            .WithMessage("Display name cannot be longer than 150 characters.");

        RuleFor(command => command.Password)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Password is required.")
            .MinimumLength(8)
            .WithMessage("Password must be at least 8 characters.")
            .MaximumLength(200)
            .WithMessage("Password cannot be longer than 200 characters.");
    }
}
```

***

## 12.4 `SetStoreOperatorStatusCommandValidator.cs`

```csharp
using FluentValidation;
using OrderManagement.Application.DTOs.Stores;

namespace OrderManagement.Application.Validators.Stores;

public sealed class SetStoreOperatorStatusCommandValidator : AbstractValidator<SetStoreOperatorStatusCommand>
{
    public SetStoreOperatorStatusCommandValidator()
    {
        RuleFor(command => command.StoreId)
            .NotEmpty()
            .WithMessage("Store id is required.");

        RuleFor(command => command.OperatorUserId)
            .NotEmpty()
            .WithMessage("Operator user id is required.");
    }
}
```

***

# 13. Store Authorization Service

File:

```text
src/OrderManagement.Application/Services/StoreAuthorizationService.cs
```

```csharp
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Stores;
using OrderManagement.Application.Constants;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Services;

public sealed class StoreAuthorizationService : IStoreAuthorizationService
{
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IStoreRepository _storeRepository;

    public StoreAuthorizationService(
        ICurrentUserContext currentUserContext,
        IStoreRepository storeRepository)
    {
        _currentUserContext = currentUserContext;
        _storeRepository = storeRepository;
    }

    public async Task EnsureCanViewStoreAsync(
        Guid storeId,
        CancellationToken cancellationToken = default)
    {
        var userId = GetRequiredUserId();
        var role = GetRequiredRole();

        if (role == UserRole.ApplicationAdmin)
        {
            return;
        }

        if (role is UserRole.SellerAdmin or UserRole.SellerOperator)
        {
            if (await _storeRepository.IsStoreOwnerAsync(storeId, userId, cancellationToken) ||
                await _storeRepository.IsStoreOperatorAsync(storeId, userId, cancellationToken))
            {
                return;
            }
        }

        throw new ForbiddenAppException("You do not have permission to view this store.");
    }

    public async Task EnsureCanManageStoreAsync(
        Guid storeId,
        CancellationToken cancellationToken = default)
    {
        var userId = GetRequiredUserId();
        var role = GetRequiredRole();

        if (role == UserRole.ApplicationAdmin)
        {
            return;
        }

        if (role == UserRole.SellerAdmin &&
            await _storeRepository.IsStoreOwnerAsync(storeId, userId, cancellationToken))
        {
            return;
        }

        throw new ForbiddenAppException("You do not have permission to manage this store.");
    }

    public async Task EnsureCanOperateStoreAsync(
        Guid storeId,
        CancellationToken cancellationToken = default)
    {
        var userId = GetRequiredUserId();
        var role = GetRequiredRole();

        if (role == UserRole.ApplicationAdmin)
        {
            return;
        }

        if (role is UserRole.SellerAdmin or UserRole.SellerOperator)
        {
            if (await _storeRepository.IsStoreOwnerAsync(storeId, userId, cancellationToken) ||
                await _storeRepository.IsStoreOperatorAsync(storeId, userId, cancellationToken))
            {
                return;
            }
        }

        throw new ForbiddenAppException("You do not have permission to operate this store.");
    }

    private Guid GetRequiredUserId()
    {
        if (!_currentUserContext.IsAuthenticated || _currentUserContext.UserId is null)
        {
            throw new UnauthorizedAppException("Authentication is required.");
        }

        return _currentUserContext.UserId.Value;
    }

    private UserRole GetRequiredRole()
    {
        return _currentUserContext.Role
            ?? throw new ForbiddenAppException("User role claim is missing.");
    }
}
```

***

# 14. StoreService

File:

```text
src/OrderManagement.Application/Services/StoreService.cs
```

```csharp
using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Stores;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.Constants;
using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Application.DTOs.Stores;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Services;

public sealed partial class StoreService : IStoreService
{
    private readonly IStoreRepository _storeRepository;
    private readonly IStoreAuthorizationService _storeAuthorizationService;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IClock _clock;
    private readonly IValidator<OpenStoreCommand> _openValidator;
    private readonly IValidator<UpdateStoreCommand> _updateValidator;
    private readonly IActivityLogWriter _activityLogWriter;
    private readonly ILogger<StoreService> _logger;

    public StoreService(
        IStoreRepository storeRepository,
        IStoreAuthorizationService storeAuthorizationService,
        ICurrentUserContext currentUserContext,
        IClock clock,
        IValidator<OpenStoreCommand> openValidator,
        IValidator<UpdateStoreCommand> updateValidator,
        IActivityLogWriter activityLogWriter,
        ILogger<StoreService> logger)
    {
        _storeRepository = storeRepository;
        _storeAuthorizationService = storeAuthorizationService;
        _currentUserContext = currentUserContext;
        _clock = clock;
        _openValidator = openValidator;
        _updateValidator = updateValidator;
        _activityLogWriter = activityLogWriter;
        _logger = logger;
    }

    public async Task<StoreDto> OpenStoreAsync(
        OpenStoreCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _openValidator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Open store request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        var userId = GetRequiredUserId();
        var role = GetRequiredRole();

        if (role is not (UserRole.Buyer or UserRole.SellerAdmin))
        {
            throw new ForbiddenAppException("Only Buyer can open a store.");
        }

        if (await _storeRepository.UserHasOwnedStoreAsync(userId, cancellationToken))
        {
            throw new ConflictAppException(
                ErrorCodes.StoreAlreadyExists,
                "User already has a store.");
        }

        var now = _clock.UtcNow;

        var store = await _storeRepository.OpenStoreAsync(
            new OpenStorePersistenceRequest
            {
                StoreId = Guid.NewGuid(),
                OwnerUserId = userId,
                StoreName = command.StoreName.Trim(),
                Slug = GenerateSlug(command.StoreName),
                Description = string.IsNullOrWhiteSpace(command.Description)
                    ? null
                    : command.Description.Trim(),
                Now = now
            },
            cancellationToken);

        _activityLogWriter.TryWrite(
            ActivityLogTypes.StoreCreated,
            metadata: new
            {
                storeId = store.Id,
                storeName = store.StoreName,
                ownerUserId = userId
            });

        _logger.LogInformation(
            "Store created. StoreId={StoreId} StoreName={StoreName} OwnerUserId={OwnerUserId}",
            store.Id,
            store.StoreName,
            userId);

        return store;
    }

    public async Task<IReadOnlyCollection<StoreDto>> GetMyStoresAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = GetRequiredUserId();
        var role = GetRequiredRole();

        if (role == UserRole.ApplicationAdmin)
        {
            return await _storeRepository.ListAllAsync(cancellationToken);
        }

        if (role is UserRole.SellerAdmin or UserRole.SellerOperator)
        {
            return await _storeRepository.ListByUserMembershipAsync(userId, cancellationToken);
        }

        return [];
    }

    public async Task<StoreDto> GetByIdAsync(
        Guid storeId,
        CancellationToken cancellationToken = default)
    {
        if (storeId == Guid.Empty)
        {
            throw new ValidationAppException(
                "Store id validation failed.",
                [AppErrorDetail.ForField("storeId", "Store id is required.")]);
        }

        var store = await _storeRepository.GetByIdAsync(storeId, cancellationToken);

        if (store is null)
        {
            throw new NotFoundAppException(
                "Store was not found.",
                ErrorCodes.StoreNotFound,
                [AppErrorDetail.ForField("storeId", "Store id does not exist.", new { storeId })]);
        }

        await _storeAuthorizationService.EnsureCanViewStoreAsync(storeId, cancellationToken);

        return store;
    }

    public async Task<StoreDto> UpdateAsync(
        UpdateStoreCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _updateValidator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Update store request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        await _storeAuthorizationService.EnsureCanManageStoreAsync(
            command.StoreId,
            cancellationToken);

        var store = await _storeRepository.UpdateStoreAsync(
            new UpdateStorePersistenceRequest
            {
                StoreId = command.StoreId,
                StoreName = command.StoreName.Trim(),
                Description = string.IsNullOrWhiteSpace(command.Description)
                    ? null
                    : command.Description.Trim(),
                Now = _clock.UtcNow
            },
            cancellationToken);

        _activityLogWriter.TryWrite(
            ActivityLogTypes.StoreUpdated,
            metadata: new
            {
                storeId = store.Id,
                storeName = store.StoreName
            });

        return store;
    }

    private Guid GetRequiredUserId()
    {
        if (!_currentUserContext.IsAuthenticated || _currentUserContext.UserId is null)
        {
            throw new UnauthorizedAppException("Authentication is required.");
        }

        return _currentUserContext.UserId.Value;
    }

    private UserRole GetRequiredRole()
    {
        return _currentUserContext.Role
            ?? throw new ForbiddenAppException("User role claim is missing.");
    }

    private static string GenerateSlug(string storeName)
    {
        var normalized = storeName.Trim().ToLowerInvariant();
        normalized = SlugUnsafeRegex().Replace(normalized, "-");
        normalized = MultipleDashRegex().Replace(normalized, "-").Trim('-');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "store";
        }

        var suffix = Guid.NewGuid().ToString("N")[..8];

        return $"{normalized}-{suffix}";
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex SlugUnsafeRegex();

    [GeneratedRegex("-+", RegexOptions.Compiled)]
    private static partial Regex MultipleDashRegex();
}
```

***

# 15. StoreOperatorService

File:

```text
src/OrderManagement.Application/Services/StoreOperatorService.cs
```

```csharp
using FluentValidation;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Stores;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.Constants;
using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Application.DTOs.Stores;
using OrderManagement.Application.Exceptions;

namespace OrderManagement.Application.Services;

public sealed class StoreOperatorService : IStoreOperatorService
{
    private readonly IStoreRepository _storeRepository;
    private readonly IStoreAuthorizationService _storeAuthorizationService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IClock _clock;
    private readonly IValidator<CreateStoreOperatorCommand> _createValidator;
    private readonly IValidator<SetStoreOperatorStatusCommand> _statusValidator;
    private readonly IActivityLogWriter _activityLogWriter;
    private readonly ILogger<StoreOperatorService> _logger;

    public StoreOperatorService(
        IStoreRepository storeRepository,
        IStoreAuthorizationService storeAuthorizationService,
        IPasswordHasher passwordHasher,
        ICurrentUserContext currentUserContext,
        IClock clock,
        IValidator<CreateStoreOperatorCommand> createValidator,
        IValidator<SetStoreOperatorStatusCommand> statusValidator,
        IActivityLogWriter activityLogWriter,
        ILogger<StoreOperatorService> logger)
    {
        _storeRepository = storeRepository;
        _storeAuthorizationService = storeAuthorizationService;
        _passwordHasher = passwordHasher;
        _currentUserContext = currentUserContext;
        _clock = clock;
        _createValidator = createValidator;
        _statusValidator = statusValidator;
        _activityLogWriter = activityLogWriter;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<StoreMemberDto>> ListOperatorsAsync(
        Guid storeId,
        CancellationToken cancellationToken = default)
    {
        if (storeId == Guid.Empty)
        {
            throw new ValidationAppException(
                "Store id validation failed.",
                [AppErrorDetail.ForField("storeId", "Store id is required.")]);
        }

        await _storeAuthorizationService.EnsureCanManageStoreAsync(storeId, cancellationToken);

        return await _storeRepository.ListOperatorsAsync(storeId, cancellationToken);
    }

    public async Task<StoreMemberDto> CreateOperatorAsync(
        CreateStoreOperatorCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _createValidator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Create store operator request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        await _storeAuthorizationService.EnsureCanManageStoreAsync(
            command.StoreId,
            cancellationToken);

        var currentUserId = _currentUserContext.UserId
            ?? throw new UnauthorizedAppException("Authentication is required.");

        var now = _clock.UtcNow;

        var member = await _storeRepository.CreateOperatorAsync(
            new CreateStoreOperatorPersistenceRequest
            {
                StoreId = command.StoreId,
                OperatorUserId = Guid.NewGuid(),
                Username = command.Username.Trim(),
                PasswordHash = _passwordHasher.HashPassword(command.Password),
                DisplayName = command.DisplayName.Trim(),
                CreatedBy = currentUserId,
                Now = now
            },
            cancellationToken);

        _activityLogWriter.TryWrite(
            ActivityLogTypes.StoreOperatorCreated,
            metadata: new
            {
                storeId = command.StoreId,
                operatorUserId = member.UserId,
                operatorUsername = member.Username,
                createdBy = currentUserId
            });

        _logger.LogInformation(
            "Store operator created. StoreId={StoreId} OperatorUserId={OperatorUserId} CreatedBy={CreatedBy}",
            command.StoreId,
            member.UserId,
            currentUserId);

        return member;
    }

    public async Task<StoreMemberDto> SetOperatorStatusAsync(
        SetStoreOperatorStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _statusValidator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Set store operator status request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        await _storeAuthorizationService.EnsureCanManageStoreAsync(
            command.StoreId,
            cancellationToken);

        var member = await _storeRepository.SetOperatorStatusAsync(
            new SetStoreOperatorStatusPersistenceRequest
            {
                StoreId = command.StoreId,
                OperatorUserId = command.OperatorUserId,
                IsActive = command.IsActive,
                Now = _clock.UtcNow
            },
            cancellationToken);

        if (!command.IsActive)
        {
            _activityLogWriter.TryWrite(
                ActivityLogTypes.StoreOperatorDeactivated,
                metadata: new
                {
                    storeId = command.StoreId,
                    operatorUserId = command.OperatorUserId
                });
        }

        return member;
    }
}
```

***

# 16. StoreRepository

File:

```text
src/OrderManagement.Infrastructure/Repositories/StoreRepository.cs
```

```csharp
using Dapper;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Constants;
using OrderManagement.Application.DTOs.Stores;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Infrastructure.Repositories;

public sealed class StoreRepository : IStoreRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public StoreRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> UserHasOwnedStoreAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT EXISTS (
                               SELECT 1
                               FROM stores
                               WHERE owner_user_id = @OwnerUserId
                           );
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new { OwnerUserId = ownerUserId },
                cancellationToken: cancellationToken));
    }

    public async Task<StoreDto?> GetByIdAsync(
        Guid storeId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT
                               id AS Id,
                               owner_user_id AS OwnerUserId,
                               store_name AS StoreName,
                               slug AS Slug,
                               description AS Description,
                               logo_url AS LogoUrl,
                               is_active AS IsActive,
                               created_at AS CreatedAt,
                               updated_at AS UpdatedAt
                           FROM stores
                           WHERE id = @StoreId
                           LIMIT 1;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<StoreDto>(
            new CommandDefinition(
                sql,
                new { StoreId = storeId },
                cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyCollection<StoreDto>> ListByUserMembershipAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT
                               s.id AS Id,
                               s.owner_user_id AS OwnerUserId,
                               s.store_name AS StoreName,
                               s.slug AS Slug,
                               s.description AS Description,
                               s.logo_url AS LogoUrl,
                               s.is_active AS IsActive,
                               s.created_at AS CreatedAt,
                               s.updated_at AS UpdatedAt
                           FROM stores s
                           INNER JOIN store_members sm ON sm.store_id = s.id
                           WHERE sm.user_id = @UserId
                             AND sm.is_active = TRUE
                           ORDER BY s.created_at DESC, s.id DESC;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<StoreDto>(
            new CommandDefinition(
                sql,
                new { UserId = userId },
                cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<IReadOnlyCollection<StoreDto>> ListAllAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT
                               id AS Id,
                               owner_user_id AS OwnerUserId,
                               store_name AS StoreName,
                               slug AS Slug,
                               description AS Description,
                               logo_url AS LogoUrl,
                               is_active AS IsActive,
                               created_at AS CreatedAt,
                               updated_at AS UpdatedAt
                           FROM stores
                           ORDER BY created_at DESC, id DESC;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<StoreDto>(
            new CommandDefinition(
                sql,
                cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<bool> IsStoreOwnerAsync(
        Guid storeId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT EXISTS (
                               SELECT 1
                               FROM store_members
                               WHERE store_id = @StoreId
                                 AND user_id = @UserId
                                 AND role = @Role
                                 AND is_active = TRUE
                           );
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new
                {
                    StoreId = storeId,
                    UserId = userId,
                    Role = StoreMemberRole.Owner.ToString()
                },
                cancellationToken: cancellationToken));
    }

    public async Task<bool> IsStoreOperatorAsync(
        Guid storeId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT EXISTS (
                               SELECT 1
                               FROM store_members
                               WHERE store_id = @StoreId
                                 AND user_id = @UserId
                                 AND role = @Role
                                 AND is_active = TRUE
                           );
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new
                {
                    StoreId = storeId,
                    UserId = userId,
                    Role = StoreMemberRole.Operator.ToString()
                },
                cancellationToken: cancellationToken));
    }

    public async Task<StoreDto> OpenStoreAsync(
        OpenStorePersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO stores
                        (id, owner_user_id, store_name, slug, description, logo_url, is_active, created_at, updated_at)
                    VALUES
                        (@StoreId, @OwnerUserId, @StoreName, @Slug, @Description, NULL, TRUE, @Now, @Now);
                    """,
                    request,
                    transaction,
                    cancellationToken: cancellationToken));

            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO store_members
                        (id, store_id, user_id, role, is_active, created_by, created_at, updated_at)
                    VALUES
                        (@Id, @StoreId, @UserId, @Role, TRUE, @CreatedBy, @Now, @Now);
                    """,
                    new
                    {
                        Id = Guid.NewGuid(),
                        request.StoreId,
                        UserId = request.OwnerUserId,
                        Role = StoreMemberRole.Owner.ToString(),
                        CreatedBy = request.OwnerUserId,
                        request.Now
                    },
                    transaction,
                    cancellationToken: cancellationToken));

            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE users
                    SET
                        role = @SellerAdminRole,
                        updated_at = @Now
                    WHERE id = @OwnerUserId
                      AND role IN (@BuyerRole, @SellerAdminRole);
                    """,
                    new
                    {
                        SellerAdminRole = UserRole.SellerAdmin.ToString(),
                        BuyerRole = UserRole.Buyer.ToString(),
                        request.OwnerUserId,
                        request.Now
                    },
                    transaction,
                    cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);

            var store = await GetByIdAsync(request.StoreId, cancellationToken);

            return store ?? throw new InvalidOperationException("Created store cannot be found.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<StoreDto> UpdateStoreAsync(
        UpdateStorePersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           UPDATE stores
                           SET
                               store_name = @StoreName,
                               description = @Description,
                               updated_at = @Now
                           WHERE id = @StoreId;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                request,
                cancellationToken: cancellationToken));

        if (affected != 1)
        {
            throw new NotFoundAppException(
                "Store was not found.",
                ErrorCodes.StoreNotFound);
        }

        var store = await GetByIdAsync(request.StoreId, cancellationToken);

        return store ?? throw new InvalidOperationException("Updated store cannot be found.");
    }

    public async Task<IReadOnlyCollection<StoreMemberDto>> ListOperatorsAsync(
        Guid storeId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT
                               sm.id AS Id,
                               sm.store_id AS StoreId,
                               sm.user_id AS UserId,
                               u.username AS Username,
                               u.display_name AS DisplayName,
                               sm.role AS Role,
                               sm.is_active AS IsActive,
                               sm.created_at AS CreatedAt,
                               sm.updated_at AS UpdatedAt
                           FROM store_members sm
                           INNER JOIN users u ON u.id = sm.user_id
                           WHERE sm.store_id = @StoreId
                             AND sm.role = @Role
                           ORDER BY sm.created_at DESC, sm.id DESC;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<StoreMemberDto>(
            new CommandDefinition(
                sql,
                new
                {
                    StoreId = storeId,
                    Role = StoreMemberRole.Operator.ToString()
                },
                cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<StoreMemberDto> CreateOperatorAsync(
        CreateStoreOperatorPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var usernameExists = await connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(
                    """
                    SELECT EXISTS (
                        SELECT 1
                        FROM users
                        WHERE lower(username) = lower(@Username)
                    );
                    """,
                    new { request.Username },
                    transaction,
                    cancellationToken: cancellationToken));

            if (usernameExists)
            {
                throw new ConflictAppException(
                    ErrorCodes.UserAlreadyExists,
                    "Username already exists.");
            }

            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO users
                        (id, username, password_hash, display_name, role, is_active, created_at, updated_at)
                    VALUES
                        (@OperatorUserId, @Username, @PasswordHash, @DisplayName, @Role, TRUE, @Now, @Now);
                    """,
                    new
                    {
                        request.OperatorUserId,
                        request.Username,
                        request.PasswordHash,
                        request.DisplayName,
                        Role = UserRole.SellerOperator.ToString(),
                        request.Now
                    },
                    transaction,
                    cancellationToken: cancellationToken));

            var memberId = Guid.NewGuid();

            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO store_members
                        (id, store_id, user_id, role, is_active, created_by, created_at, updated_at)
                    VALUES
                        (@Id, @StoreId, @UserId, @Role, TRUE, @CreatedBy, @Now, @Now);
                    """,
                    new
                    {
                        Id = memberId,
                        request.StoreId,
                        UserId = request.OperatorUserId,
                        Role = StoreMemberRole.Operator.ToString(),
                        request.CreatedBy,
                        request.Now
                    },
                    transaction,
                    cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);

            return await GetStoreMemberAsync(memberId, cancellationToken)
                   ?? throw new InvalidOperationException("Created store operator cannot be found.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<StoreMemberDto> SetOperatorStatusAsync(
        SetStoreOperatorStatusPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           UPDATE store_members
                           SET
                               is_active = @IsActive,
                               updated_at = @Now
                           WHERE store_id = @StoreId
                             AND user_id = @OperatorUserId
                             AND role = @Role
                           RETURNING id;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var memberId = await connection.ExecuteScalarAsync<Guid?>(
            new CommandDefinition(
                sql,
                new
                {
                    request.StoreId,
                    request.OperatorUserId,
                    request.IsActive,
                    request.Now,
                    Role = StoreMemberRole.Operator.ToString()
                },
                cancellationToken: cancellationToken));

        if (memberId is null)
        {
            throw new NotFoundAppException(
                "Store operator was not found.",
                ErrorCodes.UserNotFound);
        }

        return await GetStoreMemberAsync(memberId.Value, cancellationToken)
               ?? throw new InvalidOperationException("Updated store operator cannot be found.");
    }

    private async Task<StoreMemberDto?> GetStoreMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT
                               sm.id AS Id,
                               sm.store_id AS StoreId,
                               sm.user_id AS UserId,
                               u.username AS Username,
                               u.display_name AS DisplayName,
                               sm.role AS Role,
                               sm.is_active AS IsActive,
                               sm.created_at AS CreatedAt,
                               sm.updated_at AS UpdatedAt
                           FROM store_members sm
                           INNER JOIN users u ON u.id = sm.user_id
                           WHERE sm.id = @MemberId
                           LIMIT 1;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<StoreMemberDto>(
            new CommandDefinition(
                sql,
                new { MemberId = memberId },
                cancellationToken: cancellationToken));
    }
}
```

***

# 17. Application DI Update

File:

```text
src/OrderManagement.Application/DependencyInjection.cs
```

Tambahkan using:

```csharp
using OrderManagement.Application.Abstractions.Stores;
using OrderManagement.Application.DTOs.Stores;
using OrderManagement.Application.Validators.Stores;
```

Tambahkan registrations:

```csharp
services.AddScoped<IStoreService, StoreService>();
services.AddScoped<IStoreOperatorService, StoreOperatorService>();
services.AddScoped<IStoreAuthorizationService, StoreAuthorizationService>();

services.AddScoped<IValidator<OpenStoreCommand>, OpenStoreCommandValidator>();
services.AddScoped<IValidator<UpdateStoreCommand>, UpdateStoreCommandValidator>();
services.AddScoped<IValidator<CreateStoreOperatorCommand>, CreateStoreOperatorCommandValidator>();
services.AddScoped<IValidator<SetStoreOperatorStatusCommand>, SetStoreOperatorStatusCommandValidator>();
```

***

# 18. Infrastructure DI Update

File:

```text
src/OrderManagement.Infrastructure/DependencyInjection.cs
```

Tambahkan:

```csharp
services.AddScoped<IStoreRepository, StoreRepository>();
```

Pastikan using:

```csharp
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Infrastructure.Repositories;
```

***

# 19. API Contracts

Buat folder:

```text
src/OrderManagement.Api/Contracts/Stores
```

***

## 19.1 `OpenStoreRequest.cs`

```csharp
namespace OrderManagement.Api.Contracts.Stores;

public sealed class OpenStoreRequest
{
    public string StoreName { get; init; } = string.Empty;

    public string? Description { get; init; }
}
```

***

## 19.2 `UpdateStoreRequest.cs`

```csharp
namespace OrderManagement.Api.Contracts.Stores;

public sealed class UpdateStoreRequest
{
    public string StoreName { get; init; } = string.Empty;

    public string? Description { get; init; }
}
```

***

## 19.3 `StoreResponse.cs`

```csharp
namespace OrderManagement.Api.Contracts.Stores;

public sealed class StoreResponse
{
    public Guid Id { get; init; }

    public Guid OwnerUserId { get; init; }

    public string StoreName { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string? LogoUrl { get; init; }

    public bool IsActive { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
```

***

## 19.4 `CreateStoreOperatorRequest.cs`

```csharp
namespace OrderManagement.Api.Contracts.Stores;

public sealed class CreateStoreOperatorRequest
{
    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;
}
```

***

## 19.5 `SetStoreOperatorStatusRequest.cs`

```csharp
namespace OrderManagement.Api.Contracts.Stores;

public sealed class SetStoreOperatorStatusRequest
{
    public bool IsActive { get; init; }
}
```

***

## 19.6 `StoreMemberResponse.cs`

```csharp
namespace OrderManagement.Api.Contracts.Stores;

public sealed class StoreMemberResponse
{
    public Guid Id { get; init; }

    public Guid StoreId { get; init; }

    public Guid UserId { get; init; }

    public string Username { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
```

***

# 20. Controllers

## 20.1 `StoresController.cs`

File:

```text
src/OrderManagement.Api/Controllers/StoresController.cs
```

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Contracts.Stores;
using OrderManagement.Api.Extensions;
using OrderManagement.Application.Abstractions.Stores;
using OrderManagement.Application.DTOs.Stores;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AuthenticatedUser)]
[Route("api/v1/stores")]
public sealed class StoresController : ControllerBase
{
    private readonly IStoreService _storeService;

    public StoresController(IStoreService storeService)
    {
        _storeService = storeService;
    }

    [HttpPost("open")]
    [Authorize(Policy = AuthorizationPolicies.BuyerOrSellerAdmin)]
    [ProducesResponseType(typeof(StoreResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StoreResponse>> OpenStore(
        [FromBody] OpenStoreRequest request,
        CancellationToken cancellationToken)
    {
        var store = await _storeService.OpenStoreAsync(
            new OpenStoreCommand
            {
                StoreName = request.StoreName,
                Description = request.Description
            },
            cancellationToken);

        return Ok(MapStore(store));
    }

    [HttpGet("my")]
    [ProducesResponseType(typeof(IReadOnlyCollection<StoreResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<StoreResponse>>> GetMyStores(
        CancellationToken cancellationToken)
    {
        var stores = await _storeService.GetMyStoresAsync(cancellationToken);

        return Ok(stores.Select(MapStore).ToArray());
    }

    [HttpGet("{storeId:guid}")]
    [ProducesResponseType(typeof(StoreResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StoreResponse>> GetById(
        Guid storeId,
        CancellationToken cancellationToken)
    {
        var store = await _storeService.GetByIdAsync(storeId, cancellationToken);

        return Ok(MapStore(store));
    }

    [HttpPatch("{storeId:guid}")]
    [ProducesResponseType(typeof(StoreResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StoreResponse>> Update(
        Guid storeId,
        [FromBody] UpdateStoreRequest request,
        CancellationToken cancellationToken)
    {
        var store = await _storeService.UpdateAsync(
            new UpdateStoreCommand
            {
                StoreId = storeId,
                StoreName = request.StoreName,
                Description = request.Description
            },
            cancellationToken);

        return Ok(MapStore(store));
    }

    private static StoreResponse MapStore(StoreDto store)
    {
        return new StoreResponse
        {
            Id = store.Id,
            OwnerUserId = store.OwnerUserId,
            StoreName = store.StoreName,
            Slug = store.Slug,
            Description = store.Description,
            LogoUrl = store.LogoUrl,
            IsActive = store.IsActive,
            CreatedAt = store.CreatedAt,
            UpdatedAt = store.UpdatedAt
        };
    }
}
```

***

## 20.2 `StoreOperatorsController.cs`

File:

```text
src/OrderManagement.Api/Controllers/StoreOperatorsController.cs
```

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Contracts.Stores;
using OrderManagement.Api.Extensions;
using OrderManagement.Application.Abstractions.Stores;
using OrderManagement.Application.DTOs.Stores;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.StoreBackofficeUser)]
[Route("api/v1/stores/{storeId:guid}/operators")]
public sealed class StoreOperatorsController : ControllerBase
{
    private readonly IStoreOperatorService _storeOperatorService;

    public StoreOperatorsController(IStoreOperatorService storeOperatorService)
    {
        _storeOperatorService = storeOperatorService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<StoreMemberResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<StoreMemberResponse>>> List(
        Guid storeId,
        CancellationToken cancellationToken)
    {
        var operators = await _storeOperatorService.ListOperatorsAsync(
            storeId,
            cancellationToken);

        return Ok(operators.Select(MapMember).ToArray());
    }

    [HttpPost]
    [ProducesResponseType(typeof(StoreMemberResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StoreMemberResponse>> Create(
        Guid storeId,
        [FromBody] CreateStoreOperatorRequest request,
        CancellationToken cancellationToken)
    {
        var member = await _storeOperatorService.CreateOperatorAsync(
            new CreateStoreOperatorCommand
            {
                StoreId = storeId,
                Username = request.Username,
                Password = request.Password,
                DisplayName = request.DisplayName
            },
            cancellationToken);

        return Ok(MapMember(member));
    }

    [HttpPatch("{operatorUserId:guid}/status")]
    [ProducesResponseType(typeof(StoreMemberResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StoreMemberResponse>> SetStatus(
        Guid storeId,
        Guid operatorUserId,
        [FromBody] SetStoreOperatorStatusRequest request,
        CancellationToken cancellationToken)
    {
        var member = await _storeOperatorService.SetOperatorStatusAsync(
            new SetStoreOperatorStatusCommand
            {
                StoreId = storeId,
                OperatorUserId = operatorUserId,
                IsActive = request.IsActive
            },
            cancellationToken);

        return Ok(MapMember(member));
    }

    private static StoreMemberResponse MapMember(StoreMemberDto member)
    {
        return new StoreMemberResponse
        {
            Id = member.Id,
            StoreId = member.StoreId,
            UserId = member.UserId,
            Username = member.Username,
            DisplayName = member.DisplayName,
            Role = member.Role,
            IsActive = member.IsActive,
            CreatedAt = member.CreatedAt,
            UpdatedAt = member.UpdatedAt
        };
    }
}
```

***

# 21. Update Existing Business Role References

Karena enum lama hilang, update referensi lama.

## 21.1 OrderService

Replace role checks:

```csharp
currentRole == UserRole.Customer
```

menjadi:

```csharp
currentRole == UserRole.Buyer
```

Tapi SellerAdmin tetap bisa jadi buyer. Jadi untuk create order lebih baik:

```csharp
if (currentRole == UserRole.SellerOperator)
{
    throw new ForbiddenAppException("Seller operator cannot create buyer order.");
}

if (currentRole is UserRole.Buyer or UserRole.SellerAdmin &&
    command.CustomerId != currentUserId)
{
    throw new ForbiddenAppException("Buyer can only create order for themselves.");
}
```

Untuk List orders secured query:

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
    : normalizedQuery;
```

Untuk update status sementara Batch 16A:

```csharp
if (currentRole != UserRole.ApplicationAdmin)
{
    throw new ForbiddenAppException("Only Application Admin can update order status until store order ownership is enabled.");
}
```

> Batch 16D nanti kita expand ke SellerAdmin/SellerOperator untuk own store orders.

Untuk cancel:

```csharp
// Buyer-like users can cancel own order.
// ApplicationAdmin can cancel all.
// Seller roles for store orders will be enabled in Batch 16D.
```

***

## 21.2 PaymentRepository

Replace checks:

```csharp
request.RequestedByRole == UserRole.Customer
```

menjadi:

```csharp
request.RequestedByRole is UserRole.Buyer or UserRole.SellerAdmin
```

SellerOperator cannot pay:

```csharp
if (request.RequestedByRole == UserRole.SellerOperator)
{
    throw new ForbiddenAppException("Seller operator cannot create buyer payment.");
}
```

ApplicationAdmin can operationally process payment if needed.

***

## 21.3 Cancel Policy

File:

```text
src/OrderManagement.Application/Services/OrderCancellationPolicy.cs
```

Replace logic Customer with Buyer-like:

```csharp
private static bool IsBuyerLike(UserRole role)
{
    return role is UserRole.Buyer or UserRole.SellerAdmin;
}
```

Default:

```csharp
return IsBuyerLike(currentRole)
    ? OrderCancellationReason.CustomerRequested
    : OrderCancellationReason.OperationalIssue;
```

Restriction:

```csharp
if (IsBuyerLike(currentRole) &&
    parsed != OrderCancellationReason.CustomerRequested)
{
    throw new ForbiddenAppException("Buyer can only cancel with CustomerRequested reason.");
}
```

SellerOperator should not use buyer cancel until Batch 16D.

***

# 22. Seed Users Update

Replace:

```text
db/seed/001_seed_users.sql
```

```sql
INSERT INTO users (username, password_hash, display_name, role, is_active)
VALUES
    ('appadmin', crypt('Password123!', gen_salt('bf', 10)), 'Application Admin', 'ApplicationAdmin', TRUE),
    ('devops', crypt('Password123!', gen_salt('bf', 10)), 'DevOps User', 'DevOps', TRUE),
    ('selleradmin1', crypt('Password123!', gen_salt('bf', 10)), 'Seller Admin One', 'SellerAdmin', TRUE),
    ('buyer1', crypt('Password123!', gen_salt('bf', 10)), 'Buyer One', 'Buyer', TRUE),
    ('buyer2', crypt('Password123!', gen_salt('bf', 10)), 'Buyer Two', 'Buyer', TRUE)
ON CONFLICT (username) DO NOTHING;
```

***

## Optional Seed Store

Create:

```text
db/seed/003_seed_stores.sql
```

```sql
WITH seller AS (
    SELECT id
    FROM users
    WHERE username = 'selleradmin1'
    LIMIT 1
),
inserted_store AS (
    INSERT INTO stores (owner_user_id, store_name, slug, description, is_active)
    SELECT
        seller.id,
        'Seller One Store',
        'seller-one-store',
        'Default seeded seller store.',
        TRUE
    FROM seller
    ON CONFLICT (slug) DO NOTHING
    RETURNING id, owner_user_id
)
INSERT INTO store_members (store_id, user_id, role, is_active, created_by)
SELECT
    inserted_store.id,
    inserted_store.owner_user_id,
    'Owner',
    TRUE,
    inserted_store.owner_user_id
FROM inserted_store
ON CONFLICT (store_id, user_id) DO NOTHING;
```

***

# 23. Build

Run:

```bash
dotnet build
```

Kalau compile error muncul dari role lama:

```text
UserRole.Customer
UserRole.Admin
UserRole.Ops
```

Cari:

```bash
grep -R "UserRole.Customer\\|UserRole.Admin\\|UserRole.Ops" -n src tests
```

Replace:

```text
Customer -> Buyer
Admin -> ApplicationAdmin
Ops -> DevOps or SellerOperator depending context
```

Rule cepat:

```text
Business buyer flow:
  Customer -> Buyer or SellerAdmin as buyer-like.

Internal admin flow:
  Admin -> ApplicationAdmin.

Observability/log flow:
  Admin/Ops -> ApplicationAdmin/DevOps.

Seller operation flow:
  Will be enabled in Batch 16B-16D with store ownership checks.
```

***

# 24. Manual Test

## Login buyer

```bash
curl -k -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"buyer1","password":"Password123!"}' | jq
```

## Open store

```bash
BUYER_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"buyer1","password":"Password123!"}')

BUYER_TOKEN=$(echo "$BUYER_LOGIN" | jq -r '.accessToken')

curl -k -X POST https://localhost:7000/api/v1/stores/open \
  -H "Authorization: Bearer $BUYER_TOKEN" \
  -H "Content-Type: application/json" \
  -H "X-Correlation-ID: open-store-001" \
  -d '{
    "storeName": "Buyer One Store",
    "description": "Toko milik Buyer One"
  }' | jq
```

Expected:

```json
{
  "id": "...",
  "ownerUserId": "...",
  "storeName": "Buyer One Store",
  "slug": "buyer-one-store-xxxx",
  "description": "Toko milik Buyer One",
  "isActive": true
}
```

## Login ulang buyer1

Karena role sudah berubah jadi SellerAdmin, login ulang supaya JWT claim role terbaru:

```bash
SELLER_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"buyer1","password":"Password123!"}')

SELLER_TOKEN=$(echo "$SELLER_LOGIN" | jq -r '.accessToken')
```

## My stores

```bash
curl -k https://localhost:7000/api/v1/stores/my \
  -H "Authorization: Bearer $SELLER_TOKEN" | jq
```

## Create operator

```bash
STORE_ID="<store-id>"

curl -k -X POST "https://localhost:7000/api/v1/stores/$STORE_ID/operators" \
  -H "Authorization: Bearer $SELLER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "username": "operator-store-1",
    "password": "Password123!",
    "displayName": "Operator Store 1"
  }' | jq
```

## Login operator

```bash
curl -k -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"operator-store-1","password":"Password123!"}' | jq
```

Expected role:

```text
SellerOperator
```

***

# 25. Security Acceptance

Harus sesuai:

```text
Buyer bisa open store.
Setelah open store, user menjadi SellerAdmin.
SellerAdmin bisa create SellerOperator.
SellerOperator tidak register public.
SellerOperator tidak boleh buyer flow.
ApplicationAdmin bisa view/manage store.
DevOps tidak bisa manage store.
```

Tidak boleh:

```text
SellerOperator create order sebagai buyer.
DevOps create/update store.
Buyer create operator.
Customer lama role name masih dipakai.
```

***

# 26. Commit

```bash
git add .
git commit -m "feat: add store ownership and seller role model"
```

***