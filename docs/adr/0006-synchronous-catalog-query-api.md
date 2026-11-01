# ADR 0006: Synchronous Inter-Module Query API (`ICatalogApi`) vs Integration Events

## Status
Accepted

## Context
When a customer places an order in the `ordering` module, the incoming payload contains an array of `ProductId` and requested `Quantity`. The order module must validate:
1. Do these products exist and are they active?
2. What are their current authoritative prices in USD?

Allowing customers to supply prices in the HTTP request body is an obvious vulnerability (price tampering). The order must capture immutable price snapshots from the catalog.

We considered two architectural alternatives:
1. **Eventual Consistency / Data Replication**: The `ordering` module consumes `ProductCreated` and `ProductPriceChanged` events, maintaining a local replica of product prices in `ordering.product_prices`.
2. **Synchronous Query Interface**: Declaring a read-only query interface (`ICatalogApi`) in `Catalog.Contracts` that `ordering` can invoke in-process during order placement.

## Decision
I chose a **Synchronous Query Interface (`ICatalogApi`)**:
- Declared in `Shop.Modules.Catalog.Contracts`:
  ```csharp
  public interface ICatalogApi
  {
      Task<IReadOnlyDictionary<Guid, ProductSnapshot>> GetProductSnapshotsAsync(
          IEnumerable<Guid> productIds,
          CancellationToken cancellationToken = default);
  }
  ```
- Implemented internally in `Shop.Modules.Catalog` by `CatalogApi`.
- Injected into `PlaceOrderHandler` in `ordering`.

### Why this design?
1. **Read-Only / Side-Effect Free**: Queries cannot mutate state; they only return an immutable DTO (`ProductSnapshot`).
2. **Authoritative Point-in-Time Pricing**: Order placement requires the exact current catalog price at the moment the customer checks out. Eventual consistency replicates data unnecessarily for an in-process monolith.
3. **Extraction Readability**: If `catalog` is extracted into an independent microservice, `ICatalogApi` can be implemented by an HTTP `HttpClient` or gRPC client in `ordering` with zero changes to `ordering`'s domain or application logic!
4. **Boundary Preserved**: `ordering` does NOT reference `CatalogDbContext` or any entities from `catalog`. It only receives `IReadOnlyDictionary<Guid, ProductSnapshot>`.

## Consequences
### Positive
- Guarantees fresh, tamper-proof prices without maintaining redundant replicated read tables across schemas.
- In-process call incurs microsecond overhead.
- Pristine boundary: verified by architecture rule `A9_HandlerConstructorDependencies_AreIsolated_ForExtractionReadiness`.

### Negative
- Direct temporal coupling during order creation: if `catalog` query fails (e.g. database error), order placement fails immediately. For an in-process system sharing a database engine, this is acceptable.
