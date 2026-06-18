WITH store AS (
    SELECT id
    FROM stores
    WHERE slug = 'seller-one-store'
    LIMIT 1
)
UPDATE products
SET
    store_id = store.id,
    description = CASE
        WHEN products.sku = 'PRD-MOUSE-001' THEN 'Wireless mouse suitable for productivity and daily use.'
        WHEN products.sku = 'PRD-KEYBOARD-001' THEN 'Mechanical keyboard for office and gaming.'
        WHEN products.sku = 'PRD-HEADSET-001' THEN 'Gaming headset with clear sound quality.'
        ELSE products.description
    END,
    primary_image_url = CASE
        WHEN products.sku = 'PRD-MOUSE-001' THEN '/uploads/products/placeholder-mouse.webp'
        WHEN products.sku = 'PRD-KEYBOARD-001' THEN '/uploads/products/placeholder-keyboard.webp'
        WHEN products.sku = 'PRD-HEADSET-001' THEN '/uploads/products/placeholder-headset.webp'
        ELSE products.primary_image_url
    END
FROM store
WHERE products.store_id IS NULL;