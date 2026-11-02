#!/usr/bin/env bash
set -euo pipefail

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    CREATE ROLE shop_migrator LOGIN PASSWORD '${SHOP_MIGRATOR_PASSWORD}';
    REVOKE ALL ON SCHEMA public FROM PUBLIC;

    -- catalog
    CREATE ROLE catalog_app LOGIN PASSWORD '${CATALOG_APP_PASSWORD}';
    CREATE SCHEMA catalog AUTHORIZATION shop_migrator;
    GRANT USAGE ON SCHEMA catalog TO catalog_app;
    ALTER DEFAULT PRIVILEGES FOR ROLE shop_migrator IN SCHEMA catalog
        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO catalog_app;
    ALTER DEFAULT PRIVILEGES FOR ROLE shop_migrator IN SCHEMA catalog
        GRANT USAGE, SELECT ON SEQUENCES TO catalog_app;

    -- inventory
    CREATE ROLE inventory_app LOGIN PASSWORD '${INVENTORY_APP_PASSWORD}';
    CREATE SCHEMA inventory AUTHORIZATION shop_migrator;
    GRANT USAGE ON SCHEMA inventory TO inventory_app;
    ALTER DEFAULT PRIVILEGES FOR ROLE shop_migrator IN SCHEMA inventory
        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO inventory_app;
    ALTER DEFAULT PRIVILEGES FOR ROLE shop_migrator IN SCHEMA inventory
        GRANT USAGE, SELECT ON SEQUENCES TO inventory_app;

    -- ordering
    CREATE ROLE ordering_app LOGIN PASSWORD '${ORDERING_APP_PASSWORD}';
    CREATE SCHEMA ordering AUTHORIZATION shop_migrator;
    GRANT USAGE ON SCHEMA ordering TO ordering_app;
    ALTER DEFAULT PRIVILEGES FOR ROLE shop_migrator IN SCHEMA ordering
        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO ordering_app;
    ALTER DEFAULT PRIVILEGES FOR ROLE shop_migrator IN SCHEMA ordering
        GRANT USAGE, SELECT ON SEQUENCES TO ordering_app;

    -- payments
    CREATE ROLE payments_app LOGIN PASSWORD '${PAYMENTS_APP_PASSWORD}';
    CREATE SCHEMA payments AUTHORIZATION shop_migrator;
    GRANT USAGE ON SCHEMA payments TO payments_app;
    ALTER DEFAULT PRIVILEGES FOR ROLE shop_migrator IN SCHEMA payments
        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO payments_app;
    ALTER DEFAULT PRIVILEGES FOR ROLE shop_migrator IN SCHEMA payments
        GRANT USAGE, SELECT ON SEQUENCES TO payments_app;

    -- notifications
    CREATE ROLE notifications_app LOGIN PASSWORD '${NOTIFICATIONS_APP_PASSWORD}';
    CREATE SCHEMA notifications AUTHORIZATION shop_migrator;
    GRANT USAGE ON SCHEMA notifications TO notifications_app;
    ALTER DEFAULT PRIVILEGES FOR ROLE shop_migrator IN SCHEMA notifications
        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO notifications_app;
    ALTER DEFAULT PRIVILEGES FOR ROLE shop_migrator IN SCHEMA notifications
        GRANT USAGE, SELECT ON SEQUENCES TO notifications_app;
EOSQL
