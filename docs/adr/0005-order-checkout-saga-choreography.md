# ADR 0005: Order Checkout Saga Choreography with Compensations

## Status
Accepted

## Context
Checking out an order spans multiple discrete modules:
1. `ordering`: Accepts the customer order and sets status to `Pending`.
2. `inventory`: Verifies stock availability and reserves items.
3. `payments`: Charges the customer's payment method.
4. `notifications`: Informs the customer of the outcome.

Because each module owns its own database schema, a single ACID database transaction cannot span across all four modules. If a payment declines, any reserved inventory must be released. If inventory is unavailable, the order must be cancelled.

We must choose between two distributed coordination patterns:
1. **Orchestration**: A centralized saga coordinator or workflow engine (e.g., Temporal, MassTransit state machines) directing every step.
2. **Choreography**: Each module listens to integration events and reacts autonomously, publishing follow-up events or compensations.

## Decision
I selected **Event Choreography with Explicit Compensations**. For a domain of this size (five modules), choreography avoids the operational overhead and single-point-of-failure of an external orchestrator while keeping modules loosely coupled.

### Happy Path Flow
```
Ordering                     Inventory                     Payments                  Notifications
   |                             |                            |                            |
   |-- OrderPlaced ------------->|                            |                            |
   |                             |-- StockReserved ---------->|                            |
   |<----------------------------|                            |                            |
   |                                                          |-- PaymentSucceeded ------->|
   |<---------------------------------------------------------|                            |
   |-- OrderConfirmed --------------------------------------->|                            |
   |-------------------------------------------------------------------------------------->|
```

### Compensating Failure Flows
1. **Stock Shortage**:
   - `inventory` determines insufficient quantity.
   - Publishes `StockReservationFailed`.
   - `ordering` consumes `StockReservationFailed` and transitions order status to `Cancelled`.
2. **Payment Decline**:
   - `payments` attempts charge; gateway declines (e.g. limit exceeded).
   - Publishes `PaymentFailed`.
   - `ordering` transitions order to `Cancelled` and publishes `OrderCancelled`.
   - `inventory` consumes `OrderCancelled` and releases the reserved stock back to available inventory.
3. **Racing Customer Cancellation**:
   - Customer cancels order while payment is in-flight.
   - `ordering` transitions to `Cancelled` and publishes `OrderCancelled`.
   - `payments` consumes `OrderCancelled`. If payment has already succeeded, it automatically invokes gateway refund and publishes `PaymentRefunded`.

## Consequences
### Positive
- Modules remain completely decoupled; no module has knowledge of the entire global workflow.
- High throughput: modules process their portions of work asynchronously without blocking HTTP client threads.
- Comprehensive resilience: verified by automated end-to-end saga integration tests covering all compensation branches.

### Negative
- Workflow visualization requires reading multiple event handlers rather than a single orchestration file (mitigated by documentation in `docs/architecture/04-order-saga.md` and `docs/events.md`).
