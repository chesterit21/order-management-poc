UPDATE users
SET role = 'Buyer'
WHERE role = 'Customer';

UPDATE users
SET role = 'ApplicationAdmin'
WHERE role = 'Admin';

UPDATE users
SET role = 'DevOps'
WHERE role = 'Ops';

ALTER TABLE users
DROP CONSTRAINT IF EXISTS chk_users_role;

ALTER TABLE users
ADD CONSTRAINT chk_users_role
CHECK (
    role IN (
        'Buyer',
        'SellerAdmin',
        'SellerOperator',
        'ApplicationAdmin',
        'DevOps'
    )
);

CREATE TABLE IF NOT EXISTS stores (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_user_id UUID NOT NULL REFERENCES users(id),
    store_name VARCHAR(150) NOT NULL,
    slug VARCHAR(160) NOT NULL UNIQUE,
    description TEXT NULL,
    logo_url TEXT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_stores_store_name_not_empty
        CHECK (length(trim(store_name)) > 0),

    CONSTRAINT chk_stores_slug_not_empty
        CHECK (length(trim(slug)) > 0)
);

CREATE TABLE IF NOT EXISTS store_members (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    store_id UUID NOT NULL REFERENCES stores(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id),
    role VARCHAR(50) NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_by UUID NOT NULL REFERENCES users(id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_store_members_role
        CHECK (role IN ('Owner', 'Operator')),

    CONSTRAINT uq_store_members_store_user
        UNIQUE (store_id, user_id)
);

CREATE INDEX IF NOT EXISTS idx_stores_owner_user_id
ON stores(owner_user_id);

CREATE INDEX IF NOT EXISTS idx_stores_is_active
ON stores(is_active);

CREATE INDEX IF NOT EXISTS idx_store_members_store_id
ON store_members(store_id);

CREATE INDEX IF NOT EXISTS idx_store_members_user_id
ON store_members(user_id);

CREATE INDEX IF NOT EXISTS idx_store_members_role
ON store_members(role);

CREATE INDEX IF NOT EXISTS idx_store_members_is_active
ON store_members(is_active);