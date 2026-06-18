WITH seller AS (
    SELECT id
    FROM users
    WHERE username = 'selleradmin1'
    LIMIT 1
),
inserted_store AS (
    INSERT INTO stores (owner_user_id, store_name, slug, description, is_active)
    SELECT
        seller.id,
        'Seller One Store',
        'seller-one-store',
        'Default seeded seller store.',
        TRUE
    FROM seller
    ON CONFLICT (slug) DO NOTHING
    RETURNING id, owner_user_id
)
INSERT INTO store_members (store_id, user_id, role, is_active, created_by)
SELECT
    inserted_store.id,
    inserted_store.owner_user_id,
    'Owner',
    TRUE,
    inserted_store.owner_user_id
FROM inserted_store
ON CONFLICT (store_id, user_id) DO NOTHING;