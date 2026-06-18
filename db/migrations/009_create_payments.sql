CREATE TABLE IF NOT EXISTS payments (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    order_id UUID NOT NULL REFERENCES orders(id),
    amount NUMERIC(18, 2) NOT NULL,
    status VARCHAR(50) NOT NULL,
    provider VARCHAR(100) NOT NULL,
    payment_reference VARCHAR(200) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_payments_status
        CHECK (status IN ('Pending', 'Paid', 'Failed', 'Cancelled', 'RefundRequired', 'Refunded')),

    CONSTRAINT chk_payments_amount_non_negative
        CHECK (amount >= 0)
);