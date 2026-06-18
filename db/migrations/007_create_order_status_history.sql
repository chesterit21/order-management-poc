CREATE TABLE IF NOT EXISTS order_status_history (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    order_id UUID NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    from_status VARCHAR(50) NULL,
    to_status VARCHAR(50) NOT NULL,
    reason TEXT NULL,
    changed_by UUID NOT NULL REFERENCES users(id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_order_status_history_from_status
        CHECK (
            from_status IS NULL OR
            from_status IN ('Pending', 'Confirmed', 'Shipped', 'Delivered', 'Cancelled')
        ),

    CONSTRAINT chk_order_status_history_to_status
        CHECK (to_status IN ('Pending', 'Confirmed', 'Shipped', 'Delivered', 'Cancelled'))
);