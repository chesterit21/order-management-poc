-- Allow NULL for product_name_snapshot during development until application properly populates it
ALTER TABLE order_items
ALTER COLUMN product_name_snapshot DROP NOT NULL;