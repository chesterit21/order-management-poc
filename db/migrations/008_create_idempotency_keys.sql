CREATE TABLE IF NOT EXISTS idempotency_keys (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    key VARCHAR(200) NOT NULL,
    user_id UUID NOT NULL REFERENCES users(id),
    endpoint VARCHAR(200) NOT NULL,
    request_hash TEXT NOT NULL,
    status VARCHAR(50) NOT NULL,
    response_status_code INT NULL,
    response_body JSONB NULL,
    resource_type VARCHAR(100) NULL,
    resource_id UUID NULL,
    locked_until TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_idempotency_keys_status
        CHECK (status IN ('InProgress', 'Completed', 'Failed')),

    CONSTRAINT uq_idempotency_user_key_endpoint
        UNIQUE (user_id, key, endpoint)
);