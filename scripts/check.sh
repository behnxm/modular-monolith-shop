#!/usr/bin/env bash
set -euo pipefail

echo "=== 1. Checking code formatting ==="
dotnet format --verify-no-changes

echo "=== 2. Building solution in Release mode ==="
dotnet build -c Release

echo "=== 3. Running automated tests in Release mode ==="
dotnet test -c Release

echo "=== 4. Verifying EF Core model changes ==="
MODULES=("Catalog" "Inventory" "Ordering" "Payments" "Notifications")
for MOD in "${MODULES[@]}"; do
    PROJ="src/Modules/${MOD}/Shop.Modules.${MOD}/Shop.Modules.${MOD}.csproj"
    if [ -f "$PROJ" ]; then
        MIGRATIONS_DIR="src/Modules/${MOD}/Shop.Modules.${MOD}/Infrastructure/Migrations"
        # Only check if migrations directory has migration files
        if [ -d "$MIGRATIONS_DIR" ] && [ "$(find "$MIGRATIONS_DIR" -name "*.cs" | wc -l)" -gt 0 ]; then
            echo "Checking pending model changes for ${MOD}DbContext..."
            dotnet ef migrations has-pending-model-changes --project "$PROJ" --context "${MOD}DbContext"
        fi
    fi
done

echo "=== ALL CHECKS PASSED SUCCESSFULLY ==="
