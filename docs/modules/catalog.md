# Catalog Module

## What it owns
The **Catalog** module is the authoritative owner of the shop's product registry. It owns:
- Product identity (`Id`, Guid v7)
- Stock Keeping Unit (`Sku`, unique, alphanumeric)
- Product metadata (`Name`, `Description`)
- Authoritative unit price in USD (`Price`, `Currency`)
- Lifecycle status (`IsActive`, `CreatedAtUtc`, `UpdatedAtUtc`)
- The `catalog` schema in PostgreSQL (`catalog.products`, `catalog.outbox_messages`, `catalog.inbox_messages`).

## Public API
The module exposes a synchronous query API in its public contracts assembly (`Shop.Modules.Catalog.Contracts`):
- `ICatalogApi`: Injected by foreign modules (specifically `ordering`) to fetch point-in-time product snapshots without referencing catalog entities or its database context.
  ```csharp
  public interface ICatalogApi
  {
      Task<IReadOnlyDictionary<Guid, ProductSnapshot>> GetProductSnapshotsAsync(
          IEnumerable<Guid> productIds,
          CancellationToken cancellationToken = default);
  }
  ```
- `ProductSnapshot`: Read-only immutable record (`Id`, `Sku`, `Name`, `Price`, `IsActive`).

## Events it publishes
- `catalog.product-created.v1` (`ProductCreatedIntegrationEvent`): Published via the transactional outbox when a new product is created by an administrator. Consumed by the `inventory` module to initialize a zero-stock ledger entry.

## Events it consumes
- None. Catalog is an upstream master data producer.

## Endpoints and policies
All routes are grouped under `/api/v1/catalog/products`:
- `POST /api/v1/catalog/products`: Creates a new product. Policy: `shop.admin`, Rate limit: `write`. Returns `201 Created` with `Location` header.
- `PUT /api/v1/catalog/products/{id}`: Updates product name, description, and price. Policy: `shop.admin`, Rate limit: `write`. Returns `204 NoContent`.
- `DELETE /api/v1/catalog/products/{id}`: Deactivates an existing product. Policy: `shop.admin`, Rate limit: `write`. Returns `204 NoContent`.
- `GET /api/v1/catalog/products/{id}`: Retrieves product details. Policy: `shop.customer`, Rate limit: `read`. Returns `200 OK` or `404 NotFound`.
- `GET /api/v1/catalog/products`: Keyset-paginated product catalog with optional search filter. Policy: `shop.customer`, Rate limit: `read`. Returns `200 OK` with `PagedResult<ProductDto>`.

## Rules and invariants
1. **SKU Uniqueness**: SKUs are strictly unique across the system (enforced by unique database index on `catalog.products.sku`).
2. **Positive Pricing**: Product prices must be strictly greater than $0.00 and cannot exceed $1,000,000.00.
3. **Deactivated Products**: Deactivated products remain in the database for historical order reference but cannot be included in new orders.
4. **Keyset Pagination**: No `OFFSET/FETCH` pagination. Queries order by `(created_at, id)` using cursor tokens to ensure constant $O(1)$ query performance across deep datasets.

## How it is tested
- **Domain Tests** (`CatalogDomainTests.cs`): Verifies `Product` aggregate invariants, price validation, SKU validation, and deactivation rules.
- **Integration Tests** (`CatalogIntegrationTests.cs`):
  - Uses Testcontainers PostgreSQL to verify database constraints and migrations.
  - Verifies `CreateProductHandler` saves product and enqueues outbox message atomically.
  - Verifies keyset pagination forward iteration.
  - Verifies `ICatalogApi` returns valid snapshots and ignores inactive or missing IDs.
