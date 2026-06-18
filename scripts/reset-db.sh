#!/bin/bash

# Script to reset the database by applying all migrations and seed data
echo "Resetting database..."

# Navigate to the solution root (where this script is located)
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR/.."

# Make sure PostgreSQL container is running
echo "Ensuring PostgreSQL container is running..."
docker-compose up -d postgres

# Wait a moment for PostgreSQL to be ready
sleep 5

# Apply all migrations using psql
echo "Applying migrations..."
for sql_file in $(ls db/migrations/*.sql | sort); do
    echo "Executing $sql_file"
    docker-compose exec -T postgres psql -U postgres -d order_management -f "$sql_file"
done

# Apply seed data
echo "Applying seed data..."
for sql_file in $(ls db/seed/*.sql | sort); do
    echo "Executing $sql_file"
    docker-compose exec -T postgres psql -U postgres -d order_management -f "$sql_file"
done

echo "Database reset complete!"