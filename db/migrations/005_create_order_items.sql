CREATE TABLE IF NOT EXISTS order_items (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    order_id UUID NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    product_id UUID NOT NULL REFERENCES products(id),
    product_name_snapshot VARCHAR(200) NOT NULL,
    unit_price_snapshot NUMERIC(18, 2) NOT NULL,
    quantity INT NOT NULL,
    line_total NUMERIC(18, 2) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_order_items_quantity_positive
        CHECK (quantity > 0),

    CONSTRAINT chk_order_items_unit_price_non_negative
        CHECK (unit_price_snapshot >= 0),

    CONSTRAINT chk_order_items_line_total_non_negative
        CHECK (line_total >= 0)
);