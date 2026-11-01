# ADR 0003: Database per Schema with Dedicated PostgreSQL Roles

## Status
Accepted

## Context
A common failure mode in modular monoliths is data-layer coupling. Even if C# assemblies enforce internal visibility, developers might write raw SQL queries, direct joins, or create foreign key constraints between tables belonging to different modules. If Module A can query Module B's database tables directly, the module boundary is effectively an illusion, making future service extraction nearly impossible.

I needed mechanical enforcement at the database level that prevents any cross-module SQL queries, joins, or table constraints.

## Decision
I designed a **Schema-per-Module** database architecture in PostgreSQL:
1. **Dedicated Schemas**: Each module owns its own dedicated schema matching its lowercase module name (`catalog`, `inventory`, `ordering`, `payments`, `notifications`).
2. **Dedicated App Roles**: Each module runs under its own PostgreSQL application role (e.g., `catalog_app`, `inventory_app`, etc.).
3. **Migrator Role**: Schema creation and migrations are executed by a privileged `shop_migrator` user.
4. **Strict Privilege Grants**:
   - `catalog_app` is granted `USAGE` and `SELECT, INSERT, UPDATE, DELETE` *only* on schema `catalog`.
   - `inventory_app` is granted privileges *only* on schema `inventory`.
   - All modules have `REVOKE ALL ON SCHEMA public FROM PUBLIC;`.
   - Default privileges ensure any new tables created by migrations inherit this strict isolation.
5. **No Cross-Schema Foreign Keys**: Foreign keys crossing schema boundaries are strictly forbidden. Inter-module relationships are tracked solely via scalar identifiers (e.g., `CustomerId: string`, `ProductId: Guid`).
6. **Automated Verification**:
   - `tests/Shop.IntegrationTests/DatabaseIsolationIntegrationTests.cs` connects as `catalog_app` and asserts that querying any other module's tables fails with PostgreSQL error `42501` (`insufficient_privilege`).
   - A dedicated test queries `information_schema.referential_constraints` across the entire database to mechanically assert that zero cross-schema foreign keys exist.

## Consequences
### Positive
- True data isolation: impossible for any developer to execute cross-module joins or queries, even via raw ADO.NET or Dapper.
- Extraction to a completely separate PostgreSQL database instance requires zero table schema changes.
- Least-privilege security principle enforced in production.

### Negative
- Running migrations locally requires running as `shop_migrator` or granting initial privileges via `deploy/postgres/01-roles.sh`.
