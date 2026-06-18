# Hardening Notes

## Order Number

Order number is generated using PostgreSQL sequence:

```text
ORD-yyyyMMdd-sequence
```

This avoids random collision risk and is safe under concurrent requests.

## Idempotency

Current implementation inserts idempotency record before order processing and stores completed response after successful order creation.

The design protects against duplicate order creation for the same key through:

```text
UNIQUE (user_id, key, endpoint)
```

Idempotency-Key header memiliki batas panjang maksimal **200 karakter**. Request dengan key melebihi batas ini ditolak.

Future enhancement:

```text
Use shared UnitOfWork transaction so idempotency Begin, order creation, and MarkCompleted commit atomically.
```

## Cancellation Stock Restore

Cancellation reason controls stock behavior.

```text
StockUnavailable and InventoryMismatch do not restore stock.
```

This protects against overstating stock when physical stock was already sold offline or warehouse stock is mismatched.

## Payment

Payment and cancel both lock order row with FOR UPDATE. This serializes race between payment success and cancellation.

## File Upload

File upload (product images) memiliki hardening:

```text
- Maximum file size: 5 MB.
- Content type validation: hanya tipe gambar yang diizinkan.
```

Ini mencegah upload file besar atau tipe file berbahaya.

## Store Isolation

Setiap user hanya dapat mengakses data store miliknya sendiri:

```text
- Buyer hanya melihat produk dari store tempat mereka terdaftar.
- ApplicationAdmin dan StoreOperator hanya dapat mengelola store mereka sendiri.
- Data antar store tidak bocor.
```

## Security

### JWT Secret

JWT signing secret harus memiliki panjang minimum **32 bytes** (256 bits) untuk mencegah brute-force signing key.

### BCrypt Work Factor

Password hashing menggunakan BCrypt dengan work factor **12**, memberikan keseimbangan antara keamanan dan performa.

### Current Scope

Current POC uses JWT access token only. Refresh token, account lockout, and external identity provider integration are out of scope.
