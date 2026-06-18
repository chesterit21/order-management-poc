INSERT INTO products (sku, name, stock_quantity, price, is_active)
VALUES
    ('PRD-MOUSE-001', 'Mouse Wireless', 15, 150000, TRUE),
    ('PRD-KEYBOARD-001', 'Mechanical Keyboard', 20, 450000, TRUE),
    ('PRD-HEADSET-001', 'Gaming Headset', 10, 350000, TRUE)
ON CONFLICT (sku) DO NOTHING;