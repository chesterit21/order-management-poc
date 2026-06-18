-- ... existing code ...

CREATE TABLE IF NOT EXISTS activity_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    correlation_id VARCHAR(100) NOT NULL,
    activity_type VARCHAR(100) NOT NULL,

    actor_user_id UUID NULL,
    actor_username VARCHAR(100) NULL,
    actor_role VARCHAR(50) NULL,

    order_id UUID NULL,
    order_number VARCHAR(50) NULL,
    product_id UUID NULL,
    payment_id UUID NULL,

    request_path VARCHAR(500) NULL,
    http_method VARCHAR(20) NULL,
    status_code INT NULL,
    elapsed_ms BIGINT NULL,

    error_code VARCHAR(100) NULL,

    before_state JSONB NULL,
    after_state JSONB NULL,
    metadata JSONB NULL,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_activity_logs_correlation_id
ON activity_logs(correlation_id);

CREATE INDEX IF NOT EXISTS idx_activity_logs_activity_type
ON activity_logs(activity_type);

CREATE INDEX IF NOT EXISTS idx_activity_logs_actor_user_id
ON activity_logs(actor_user_id);

CREATE INDEX IF NOT EXISTS idx_activity_logs_order_id
ON activity_logs(order_id);

CREATE INDEX IF NOT EXISTS idx_activity_logs_order_number
ON activity_logs(order_number);

CREATE INDEX IF NOT EXISTS idx_activity_logs_product_id
ON activity_logs(product_id);

CREATE INDEX IF NOT EXISTS idx_activity_logs_payment_id
ON activity_logs(payment_id);

CREATE INDEX IF NOT EXISTS idx_activity_logs_created_at
ON activity_logs(created_at DESC);

CREATE INDEX IF NOT EXISTS idx_activity_logs_error_code
ON activity_logs(error_code)
WHERE error_code IS NOT NULL;