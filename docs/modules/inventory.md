# Inventory Module

## What it owns
The **Inventory** module owns stock levels and inventory reservations. It is the sole authority on item availability:
- Stock items (`StockItem`: `ProductId`, `QuantityOnHand`, `QuantityReserved`, `QuantityAvailable`)
- Stock reservations (`Reservation`: `OrderId`, `Status`: `Pending`, `Committed`, `Released`)
- Reservation line items (`ReservationLine`: `ProductId`, `Quantity`)
- The `inventory` schema in PostgreSQL (`inventory.stock_items`, `inventory.reservations`, `inventory.reservation_lines`, `inventory.outbox_messages`, `inventory.inbox_messages`).

## Public API
The module exposes no synchronous query API. All interactions with Inventory occur asynchronously via integration events.

## Events it publishes
- `inventory.stock-reserved.v1` (`StockReservedIntegrationEvent`): Published when an order's requested line items are atomically reserved. Consumed by `payments` to trigger payment capture and `ordering` to update order state.
- `inventory.stock-reservation-failed.v1` (`StockReservationFailedIntegrationEvent`): Published when one or more line items lack sufficient available inventory. Consumed by `ordering` to cancel the order.

## Events it consumes
- `catalog.product-created.v1`: Consumed to seed a `StockItem` with 0 available quantity upon product cataloging.
- `ordering.order-placed.v1`: Consumed to evaluate and perform an atomic conditional reservation for the order.
- `ordering.order-confirmed.v1`: Consumed when payment succeeds, committing the reservation (reducing both `QuantityOnHand` and `QuantityReserved`).
- `ordering.order-cancelled.v1`: Consumed when an order is cancelled or compensated, releasing `QuantityReserved` back to `QuantityAvailable`.

## Endpoints and policies
All routes are grouped under `/api/v1/inventory/stock`:
- `PUT /api/v1/inventory/stock/{productId}`: Sets or adjusts on-hand inventory quantity. Policy: `shop.admin`, Rate limit: `write`. Returns `200 OK`.
- `GET /api/v1/inventory/stock/{productId}`: Retrieves current stock levels for a product. Policy: `shop.customer`, Rate limit: `read`. Returns `200 OK` or `404 NotFound`.
- `GET /api/v1/inventory/stock`: Keyset-paginated stock inventory list. Policy: `shop.customer`, Rate limit: `read`. Returns `200 OK`.

## Rules and invariants
1. **Atomic Conditional Reservation**: Multiple parallel orders competing for the same limited stock item must never cause overselling. Enforced using an atomic conditional update query:
   ```sql
   UPDATE inventory.stock_items
   SET quantity_reserved = quantity_reserved + @quantity
   WHERE product_id = @productId
     AND (quantity_on_hand - quantity_reserved) >= @quantity;
   ```
2. **All-or-Nothing Reservation**: An order reservation either reserves 100% of the requested lines or 0%. If any line item is unavailable, previously reserved lines for that order are rolled back immediately and `StockReservationFailed` is published.
3. **Idempotent Reservation & Releases**: Handled via the transactional inbox; replayed events do not double-reserve or double-release.
4. **Out-of-Order Resiliency**: If an `OrderCancelled` event arrives before `OrderPlaced` has processed, the reservation is marked cancelled to prevent zombie reservations.

## How it is tested
- **Domain Tests** (`InventoryDomainTests.cs`): Verifies `StockItem` reserve, commit, release, and replenish invariants.
- **Concurrency Integration Tests** (`InventoryConcurrencyTests.cs`):
  - Sets stock on a product to 10 units.
  - Fires 50 simultaneous parallel reservation tasks via `Task.WhenAll`.
  - Asserts that exactly 10 units are reserved, remaining 40 fail cleanly, and zero overselling occurs.
- **Outbox/Inbox Proof Tests** (`EventingProofTests.cs`): Proves `SKIP LOCKED` lock handling and inbox deduplication.
