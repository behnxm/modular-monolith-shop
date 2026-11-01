# Ordering Module

## What it owns
The **Ordering** module is the customer checkout authority. It owns:
- Orders (`Order`: `Id`, `CustomerId`, `Status`, `TotalAmount`, `Currency`, `CreatedAtUtc`, `UpdatedAtUtc`, `CancellationReason`)
- Order line items (`OrderLine`: `Id`, `OrderId`, `ProductId`, `Quantity`, `UnitPrice`)
- Order status state machine (`Pending`, `StockReserved`, `Confirmed`, `Cancelled`)
- Idempotency ledger (`IdempotencyRecord`: `IdempotencyKey`, `CustomerId`, `RequestHash`, `OrderId`, `CreatedAtUtc`)
- The `ordering` schema in PostgreSQL (`ordering.orders`, `ordering.order_lines`, `ordering.idempotency_records`, `ordering.outbox_messages`, `ordering.inbox_messages`).

## Public API
The module exposes no synchronous query API. Foreign modules interact with Ordering asynchronously via events.

## Events it publishes
- `ordering.order-placed.v1` (`OrderPlacedIntegrationEvent`): Published when an order is created. Kicks off the checkout saga.
- `ordering.order-confirmed.v1` (`OrderConfirmedIntegrationEvent`): Published when payment succeeds, finalizing the order.
- `ordering.order-cancelled.v1` (`OrderCancelledIntegrationEvent`): Published when the customer cancels or when stock/payment compensations abort the saga.

## Events it consumes
- `inventory.stock-reserved.v1`: Transitions order state to `StockReserved` (or remains `Pending` awaiting payment).
- `inventory.stock-reservation-failed.v1`: Transitions order state to `Cancelled` with reason `"Insufficient inventory available"`.
- `payments.payment-succeeded.v1`: Transitions order state to `Confirmed` and publishes `OrderConfirmed`.
- `payments.payment-failed.v1`: Transitions order state to `Cancelled` with gateway decline reason, and publishes `OrderCancelled` to compensate inventory.

## Endpoints and policies
All routes are grouped under `/api/v1/ordering/orders`:
- `POST /api/v1/ordering/orders`: Places an order. Policy: `shop.customer`, Rate limit: `write`. Requires `Idempotency-Key` header. Returns `202 Accepted` with `Location` header.
- `GET /api/v1/ordering/orders/{id}`: Retrieves order details. Policy: `shop.customer`, Rate limit: `read`. Returns `200 OK` or `404 NotFound` (BOLA protection: returns 404 if accessed by another customer).
- `GET /api/v1/ordering/orders`: Lists the current authenticated customer's orders with keyset pagination. Policy: `shop.customer`, Rate limit: `read`.
- `GET /api/v1/ordering/orders/admin`: Lists all orders across all customers. Policy: `shop.admin`, Rate limit: `read`.
- `POST /api/v1/ordering/orders/{id}/cancel`: Cancels an order. Policy: `shop.customer`, Rate limit: `write`. Returns `204 NoContent`.

## Rules and invariants
1. **Idempotency with Payload Verification**: Incoming `Idempotency-Key` is validated against a SHA-256 hash of the order lines. Replayed identical requests return cached 202 Accepted with `Idempotent-Replayed: true`. Conflicting payloads with an existing key return `409 Conflict`.
2. **Catalog Price Integrity**: Order lines are verified against `ICatalogApi`. Prices supplied by clients are ignored; authoritative catalog snapshot prices are written to `OrderLine.UnitPrice`.
3. **Line Caps**: Orders must contain between 1 and 50 distinct lines; line quantities must be between 1 and 100 units.
4. **State Machine Invariants**: Once an order reaches a terminal state (`Confirmed` or `Cancelled`), no further state transitions are permitted. Customer cancellation is only allowed while the order is in `Pending` or `StockReserved` state.

## How it is tested
- **Domain Tests** (`OrderingDomainTests.cs`): Verifies status state machine transitions, line caps, quantity validation, and cancellation rules.
- **Integration Tests** (`OrderingIntegrationTests.cs`): Verifies `PlaceOrderHandler`, SHA-256 conflict detection, and transactional outbox persistence.
- **System Saga Tests** (`OrderSagaIntegrationTests.cs`): Full multi-module checkout saga verification.
