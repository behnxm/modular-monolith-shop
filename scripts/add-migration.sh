#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 2 ]; then
    echo "Usage: $0 <ModuleName> <MigrationName>"
    echo "Example: $0 Catalog InitialCreate"
    exit 1
fi

MODULE="$1"
MIGRATION_NAME="$2"

PROJECT="src/Modules/${MODULE}/Shop.Modules.${MODULE}/Shop.Modules.${MODULE}.csproj"
CONTEXT="${MODULE}DbContext"
OUTPUT_DIR="Infrastructure/Migrations"

if [ ! -f "$PROJECT" ]; then
    echo "Error: Project '$PROJECT' not found."
    exit 1
fi

echo "Adding migration '${MIGRATION_NAME}' for context '${CONTEXT}' in project '${PROJECT}'..."
dotnet ef migrations add "$MIGRATION_NAME" \
    --project "$PROJECT" \
    --startup-project src/Host/Shop.Host/Shop.Host.csproj \
    --context "$CONTEXT" \
    --output-dir "$OUTPUT_DIR"

echo "Migration '${MIGRATION_NAME}' added successfully."
