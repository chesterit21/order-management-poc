-- Add price column to order_items for historical price tracking
ALTER TABLE order_items
ADD COLUMN price DECIMAL(10, 2) NOT NULL;