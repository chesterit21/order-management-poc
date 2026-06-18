CREATE TABLE IF NOT EXISTS inventory_movements (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    product_id UUID NOT NULL REFERENCES products(id),
    order_id UUID NULL REFERENCES orders(id),
    movement_type VARCHAR(50) NOT NULL,
    quantity INT NOT NULL,
    stock_before INT NOT NULL,
    stock_after INT NOT NULL,
    reason TEXT NULL,
    created_by UUID NULL REFERENCES users(id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_inventory_movements_type
        CHECK (movement_type IN ('OrderCreatedDeduction', 'OrderCancelledRestore', 'ManualAdjustment')),

    CONSTRAINT chk_inventory_movements_quantity_positive
        CHECK (quantity > 0),

    CONSTRAINT chk_inventory_movements_stock_non_negative
        CHECK (stock_before >= 0 AND stock_after >= 0)
);