-- Add subtotal column to order_items for calculated line item totals
ALTER TABLE order_items
ADD COLUMN subtotal DECIMAL(10, 2) NOT NULL DEFAULT 0.00;