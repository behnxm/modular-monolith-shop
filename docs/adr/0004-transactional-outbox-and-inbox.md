# ADR 0004: In-Process Integration Events with Transactional Outbox and Inbox

## Status
Accepted

## Context
When a business action occurs inside a module (e.g., an order is placed in `ordering`), other modules need to react (e.g., `inventory` must reserve stock, `payments` must process payment).

There are three common anti-patterns for inter-module eventing:
1. **Synchronous Direct Calls**: Violates temporal decoupling and creates cascading failure modes.
2. **Dual Writes**: Updating the business entity in the database and publishing to an in-memory bus or message broker in two separate operations without a distributed transaction (2PC). If the database commits but the process crashes before publishing, the event is lost. If the event publishes but the database transaction rolls back, phantom events are dispatched.
3. **Third-Party Heavyweight Frameworks**: Using MassTransit, Wolverine, or NServiceBus adds unnecessary framework weight, opinionated abstractions, and versioning constraints for an in-process modular monolith.

## Decision
I implemented a lightweight, hand-crafted **Transactional Outbox and Inbox Pattern** backed by PostgreSQL and native .NET channels:

1. **Transactional Outbox**:
   - Each module's `ModuleDbContext` includes an `outbox_messages` table in its own schema.
   - When a use case persists state changes, it appends an `OutboxMessage` within the *same* database transaction.
   - If the business transaction fails, the event is rolled back atomically. If it commits, the event is guaranteed to be saved.
   - A dedicated background worker (`OutboxProcessor<TContext>`) polls the outbox using PostgreSQL's `SELECT ... FOR UPDATE SKIP LOCKED` idiom. This guarantees that multiple host replicas or concurrent threads can poll without lock contention or duplicate execution.
   - The processor serializes the event into an `IntegrationEventEnvelope` and dispatches it to the in-process transport.
   - Exponential backoff with jitter is applied if dispatch fails, with failure recording after maximum attempts.

2. **In-Process Event Transport (`InProcessEventTransport`)**:
   - Routes envelopes to registered handlers in foreign modules using an internal registry (`IntegrationEventRegistry`).
   - Resolves handlers via scoped dependency injection, ensuring fresh DbContext and service lifetimes per event dispatch.

3. **Transactional Inbox**:
   - Each module's `ModuleDbContext` contains an `inbox_messages` table.
   - When an event arrives at a consuming module, the `InboxDispatcher` verifies whether the event's `(MessageId, HandlerName)` has already been processed.
   - If already processed, it is skipped immediately (idempotency guarantee).
   - If new, the handler executes within a transaction that simultaneously records the inbox entry upon completion.

## Consequences
### Positive
- Guaranteed at-least-once delivery and exactly-once processing semantics without distributed transactions.
- Zero external broker dependency (no Kafka or RabbitMQ required for running the monolith).
- Total resilience against process crashes and database rollbacks.
- Fully auditable message ledger directly in each module's schema.

### Negative
- Asynchronous eventual consistency between modules: a brief delay occurs between order placement and stock reservation.
- Database storage overhead for outbox and inbox tables (mitigated by periodic cleanup of processed records).
