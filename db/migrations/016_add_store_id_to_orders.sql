ALTER TABLE orders
ADD COLUMN IF NOT EXISTS store_id UUID NULL REFERENCES stores(id);

CREATE INDEX IF NOT EXISTS idx_orders_store_id
ON orders(store_id);

CREATE INDEX IF NOT EXISTS idx_orders_store_status
ON orders(store_id, status);

CREATE INDEX IF NOT EXISTS idx_orders_store_created_at
ON orders(store_id, created_at DESC);