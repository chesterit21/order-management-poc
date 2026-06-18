ALTER TABLE products
ADD COLUMN IF NOT EXISTS store_id UUID NULL REFERENCES stores(id);

ALTER TABLE products
ADD COLUMN IF NOT EXISTS description TEXT NULL;

ALTER TABLE products
ADD COLUMN IF NOT EXISTS primary_image_url TEXT NULL;

CREATE INDEX IF NOT EXISTS idx_products_store_id
ON products(store_id);

CREATE INDEX IF NOT EXISTS idx_products_store_active
ON products(store_id, is_active);

CREATE INDEX IF NOT EXISTS idx_products_created_at
ON products(created_at DESC);

CREATE INDEX IF NOT EXISTS idx_products_name
ON products(name);