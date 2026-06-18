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