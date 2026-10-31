#!/usr/bin/env bash
set -euo pipefail

SECRETS_DIR="secrets"
mkdir -p "$SECRETS_DIR"

DEV_KEY="$SECRETS_DIR/dev-issuer.key"
if [ ! -f "$DEV_KEY" ]; then
    echo "Generating ECDSA P-256 key for development token issuer..."
    if command -v openssl >/dev/null 2>&1; then
        openssl ecparam -name prime256v1 -genkey -noout -out "$DEV_KEY"
    else
        # Fallback using dotnet if openssl is not present
        dotnet run --project src/Host/Shop.Host/Shop.Host.csproj -- --generate-dev-key "$DEV_KEY"
    fi
    chmod 600 "$DEV_KEY" 2>/dev/null || true
    echo "Dev issuer key generated at $DEV_KEY."
else
    echo "Dev issuer key already exists at $DEV_KEY."
fi

if [ ! -f ".env" ]; then
    echo "Generating .env with strong local credentials..."
    generate_secret() {
        if command -v openssl >/dev/null 2>&1; then
            openssl rand -hex 16
        else
            head -c 16 /dev/urandom | od -An -tx1 | tr -d ' \n'
        fi
    }

    PG_PASS="$(generate_secret)"
    MIGRATOR_PASS="$(generate_secret)"
    CATALOG_PASS="$(generate_secret)"
    INVENTORY_PASS="$(generate_secret)"
    ORDERING_PASS="$(generate_secret)"
    PAYMENTS_PASS="$(generate_secret)"
    NOTIFICATIONS_PASS="$(generate_secret)"

    cat <<EOF > .env
# Database Configuration
POSTGRES_DB=shop
POSTGRES_USER=postgres
POSTGRES_PASSWORD=${PG_PASS}

# Module Role Passwords
SHOP_MIGRATOR_PASSWORD=${MIGRATOR_PASS}
CATALOG_APP_PASSWORD=${CATALOG_PASS}
INVENTORY_APP_PASSWORD=${INVENTORY_PASS}
ORDERING_APP_PASSWORD=${ORDERING_PASS}
PAYMENTS_APP_PASSWORD=${PAYMENTS_PASS}
NOTIFICATIONS_APP_PASSWORD=${NOTIFICATIONS_PASS}

# Connection Strings
ConnectionStrings__Migrator=Host=localhost;Port=5432;Database=shop;Username=shop_migrator;Password=${MIGRATOR_PASS}
ConnectionStrings__catalog=Host=localhost;Port=5432;Database=shop;Username=catalog_app;Password=${CATALOG_PASS}
ConnectionStrings__inventory=Host=localhost;Port=5432;Database=shop;Username=inventory_app;Password=${INVENTORY_PASS}
ConnectionStrings__ordering=Host=localhost;Port=5432;Database=shop;Username=ordering_app;Password=${ORDERING_PASS}
ConnectionStrings__payments=Host=localhost;Port=5432;Database=shop;Username=payments_app;Password=${PAYMENTS_PASS}
ConnectionStrings__notifications=Host=localhost;Port=5432;Database=shop;Username=notifications_app;Password=${NOTIFICATIONS_PASS}

# Application Configuration
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://localhost:8080
Database__UseMigratorConnection=false

# Authentication & Development Token Issuer
DevIssuer__Enabled=true
DevIssuer__SigningKeyPath=secrets/dev-issuer.key
Authentication__Authority=https://auth.shop.local
Authentication__Audience=shop-api

# Module Outbox Configuration Defaults
Modules__catalog__Outbox__PollIntervalMs=500
Modules__inventory__Outbox__PollIntervalMs=500
Modules__ordering__Outbox__PollIntervalMs=500
Modules__payments__Outbox__PollIntervalMs=500
Modules__notifications__Outbox__PollIntervalMs=500

# Payments Mock Gateway Approval Limit
Modules__Payments__ApprovalLimit=10000

# OpenTelemetry Exporter Endpoint
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
EOF

    chmod 600 .env 2>/dev/null || true
    echo ".env created successfully."
else
    echo ".env already exists. Skipping generation."
fi

echo "Initialization completed."
