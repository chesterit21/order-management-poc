ALTER TABLE inventory_movements
DROP CONSTRAINT IF EXISTS chk_inventory_movements_type;

ALTER TABLE inventory_movements
ADD CONSTRAINT chk_inventory_movements_type
CHECK (
    movement_type IN (
        'OrderCreatedDeduction',
        'OrderCancelledRestore',
        'OrderCancelledNoRestore',
        'ManualAdjustment'
    )
);