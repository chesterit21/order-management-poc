CREATE INDEX IF NOT EXISTS idx_products_is_active
ON products(is_active);

CREATE INDEX IF NOT EXISTS idx_orders_customer_id
ON orders(customer_id);

CREATE INDEX IF NOT EXISTS idx_orders_status
ON orders(status);

CREATE INDEX IF NOT EXISTS idx_orders_created_at
ON orders(created_at);

CREATE INDEX IF NOT EXISTS idx_orders_customer_status_created
ON orders(customer_id, status, created_at);

CREATE INDEX IF NOT EXISTS idx_order_items_order_id
ON order_items(order_id);

CREATE INDEX IF NOT EXISTS idx_order_items_product_id
ON order_items(product_id);

CREATE INDEX IF NOT EXISTS idx_inventory_movements_product_id
ON inventory_movements(product_id);

CREATE INDEX IF NOT EXISTS idx_inventory_movements_order_id
ON inventory_movements(order_id);

CREATE INDEX IF NOT EXISTS idx_inventory_movements_created_at
ON inventory_movements(created_at);

CREATE INDEX IF NOT EXISTS idx_order_status_history_order_id
ON order_status_history(order_id);

CREATE INDEX IF NOT EXISTS idx_idempotency_keys_user_endpoint
ON idempotency_keys(user_id, endpoint);

CREATE INDEX IF NOT EXISTS idx_idempotency_keys_status
ON idempotency_keys(status);

CREATE INDEX IF NOT EXISTS idx_idempotency_keys_created_at
ON idempotency_keys(created_at);

CREATE INDEX IF NOT EXISTS idx_payments_order_id
ON payments(order_id);

CREATE INDEX IF NOT EXISTS idx_payments_status
ON payments(status);

CREATE UNIQUE INDEX IF NOT EXISTS uq_payments_one_paid_per_order
ON payments(order_id)
WHERE status = 'Paid';