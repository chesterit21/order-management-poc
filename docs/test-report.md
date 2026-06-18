# Test Report Notes

## Unit Tests

Covered:

```text
Payment validation
Payment NRules
Order transition NRules
Cancel eligibility NRules
Cancel reason policy
Idempotency request hash
Idempotency service behavior
```

## Integration Tests

Covered with PostgreSQL Testcontainers:

```text
Concurrent stock deduction
Idempotent create race
Concurrent status update
Payment vs cancel race
Duplicate payment prevention
```

## Key Assertions

### Concurrent Stock Deduction

Setup:

```text
Product stock = 15
Request A quantity = 10
Request B quantity = 10
```

Expected:

```text
Only one order succeeds
One order fails with conflict
Final stock = 5
Stock never negative
```

### Idempotent Create Race

Setup:

```text
Two POST /orders
Same user
Same Idempotency-Key
Same payload
```

Expected:

```text
Only one order created
Only one stock deduction
Second request gets either stored response or request-in-progress conflict
```

### Concurrent Status Update

Setup:

```text
Order Confirmed
Admin A -> Shipped
Admin B -> Cancel
```

Expected:

```text
Only one operation succeeds
Final status consistent
Loser receives conflict or business rule error
```

### Payment vs Cancel Race

Expected:

```text
Payment wins:
  order confirmed
  cancel may later mark payment refund required

Cancel wins:
  order cancelled
  payment rejected
```

### Duplicate Payment Prevention

Expected:

```text
Only one Paid payment exists
Order status Confirmed
Second payment rejected
```

## Production Notes

The tests demonstrate that the API protects critical sections through PostgreSQL row-level locking, row version checks, unique constraints, and business rules.