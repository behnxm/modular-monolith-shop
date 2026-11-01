# Senior & Principal Architecture Interview Notes

These notes capture the key design decisions, trade-offs, and technical rationale underpinning **ModularMonolithShop**, formulated as comprehensive responses to senior and principal engineering interview questions.

---

## 1. Modular Monolith vs Microservices

### Q: Why build a modular monolith rather than microservices from day one?
**Response**:
"In my experience, microservices solve organizational scaling problems (multiple autonomous teams touching independent codebases), not architectural code quality problems. When early-stage or mid-sized teams adopt microservices prematurely, they exchange in-process method calls for network hops, atomic database transactions for eventual consistency sagas, and simple debugging for distributed tracing pipelines.

A disciplined modular monolith provides the best of both worlds:
- **Clean Domain Boundaries**: Each module has hard physical boundaries (assembly pairs, internal visibility, dedicated schemas).
- **High Developer Velocity**: Single solution, single container, atomic deployments, in-process call performance, and instantaneous integration testing.
- **Cheap Extraction**: If business metrics demand that a specific module scale independently (e.g. 10x traffic to Payments), that module can be extracted into an independent microservice in hours because zero cross-module code or database couplings exist."

### Q: When would you actually extract a module?
**Response**:
"I would extract a module only under three specific conditions:
1. **Divergent Scaling**: The module requires specialized hardware (e.g., GPU nodes for AI) or 100x higher CPU/RAM scaling than the rest of the monolith.
2. **Team Topology (Conway's Law)**: A dedicated team of 8+ engineers owns that specific subdomain and requires independent CI/CD deployment cadences without coordinating releases with other teams.
3. **Regulatory / Security Boundaries**: The module handles PCI-DSS payment data or HIPAA medical records and must reside within a tightly scoped, audited security perimeter."

---

## 2. Boundary Enforcement

### Q: How do you prevent developers from 'cheating' and breaking module boundaries?
**Response**:
"I don't rely on code reviews or documentation guidelines alone; I enforce boundaries mechanically at four layers:
1. **Project References**: Module implementation assemblies cannot reference each other.
2. **Compiler Visibility**: Types are declared `internal` by default. Only the `IModule` class is exported.
3. **Automated Architecture Tests**: Using NetArchTest in CI (`tests/Shop.ArchitectureTests`), rules A1 through A10 verify that:
   - No foreign types are injected into handlers.
   - BuildingBlocks never reference modules.
   - Handlers never touch `HttpContext` or foreign `DbContext`s.
   - Role literals (`[Authorize(Roles = ...)]`) are forbidden.
4. **Database Engine Permissions**: In PostgreSQL, each module owns a dedicated schema. Application roles (`catalog_app`, `inventory_app`, etc.) have `REVOKE ALL` on foreign schemas. An automated test in CI asserts that querying across schemas throws PostgreSQL error `42501` (`insufficient_privilege`), and that zero cross-schema foreign keys exist."

---

## 3. Asynchronous Messaging & Concurrency

### Q: How does the Transactional Outbox work, and why use `FOR UPDATE SKIP LOCKED`?
**Response**:
"To avoid the dual-write problem (writing to a database and publishing to a message broker in separate uncoordinated operations), the business entity and an `OutboxMessage` record are persisted within the exact same ACID transaction.

A background `OutboxProcessor` polls the outbox table. If multiple host instances or threads poll simultaneously without coordination, naive queries cause lock contention or duplicate event dispatch. By using PostgreSQL's `FOR UPDATE SKIP LOCKED`:
- The database locks only the fetched rows and skips already-locked rows without blocking.
- Multiple instances process mutually exclusive batches concurrently with zero deadlocks and zero wait time.
- Once dispatched, messages are stamped `processed_on = NOW()`."

### Q: How do you prevent inventory overselling when 50 customers purchase the last 10 items simultaneously?
**Response**:
"I avoid naive read-modify-write patterns (`var item = await GetAsync(); item.Quantity -= q; await SaveChangesAsync();`), which create severe race conditions unless wrapped in serializable transactions with high abort rates.

Instead, I use an **atomic conditional SQL update**:
```sql
UPDATE inventory.stock_items
SET quantity_reserved = quantity_reserved + @quantity
WHERE product_id = @productId
  AND (quantity_on_hand - quantity_reserved) >= @quantity;
```
PostgreSQL acquires an exclusive row-level lock during the update. If the condition `(quantity_on_hand - quantity_reserved) >= @quantity` evaluates to true, the rows affected is 1 and the reservation succeeds. If insufficient stock remains, rows affected is 0 and the reservation fails immediately.

I proved this in `tests/Shop.Modules.Inventory.Tests/InventoryConcurrencyTests.cs`, where 50 parallel tasks compete for 10 units of stock. Exactly 10 units are reserved, 40 fail cleanly, and zero overselling occurs."

### Q: How do you handle a race condition where a customer cancels an order while payment is being charged?
**Response**:
"In an event-choreographed saga, customer cancellation can race against payment capture.
I handle this in the `payments` module's `OrderCancelledIntegrationEventHandler`:
- When `OrderCancelled` arrives, the handler checks the state of the payment attempt.
- If payment is still `Pending`, it transitions to `Cancelled` and aborts the charge.
- If payment has already completed (`Succeeded`), the handler immediately calls `IPaymentGateway.RefundAsync()`, marks the payment `Refunded`, and publishes `PaymentRefundedIntegrationEvent`.
This guarantees that money is never held for a cancelled order."

---

## 4. Idempotency & Resiliency

### Q: Why hash the request payload in the idempotency ledger?
**Response**:
"Standard idempotency stores only the `Idempotency-Key` and returns the cached response on duplicate requests. However, this allows a critical bug: if a client mistakenly or maliciously sends a completely different payload using an existing idempotency key, returning the original response would silently mask an invalid operation.

In `PlaceOrderHandler`, I compute a deterministic SHA-256 hash of the normalized request lines.
- If the incoming key exists and the `request_hash` matches: it is a genuine network retry. The existing order is returned with header `Idempotent-Replayed: true`.
- If the incoming key exists but the `request_hash` differs: the request is rejected with `HTTP 409 Conflict`, informing the client that the key was already used for a different order."

### Q: How does the Transactional Inbox achieve exactly-once processing?
**Response**:
"Message transports guarantee at-least-once delivery. To achieve exactly-once processing, each consuming module maintains an `inbox_messages` table with a unique constraint on `(message_id, handler_name)`.

When an event arrives:
1. `InboxDispatcher` checks if `(message_id, handler_name)` already exists. If yes, it skips execution immediately.
2. If new, the handler executes within a transaction that inserts the inbox record simultaneously with the business state mutation.
3. If the handler fails, the transaction rolls back, leaving the inbox empty so subsequent retries can succeed."

---

## 5. Security & Observability

### Q: How do you prevent Broken Object Level Authorization (BOLA)?
**Response**:
"BOLA (formerly IDOR) occurs when an API accepts an object ID from a client and retrieves it without verifying ownership.

In this architecture:
- Endpoints never trust the client's identity claims from request bodies. Identity is extracted exclusively from the authenticated JWT `sub` claim via `ICurrentUser`.
- Queries filter on compound predicates: `WHERE order.Id == id AND order.CustomerId == currentUser.UserId`.
- If an order exists but belongs to another customer, the handler returns **HTTP 404 Not Found**, never HTTP 403 Forbidden. This prevents attackers from enumerating valid IDs through response code differences."

### Q: How does distributed tracing work across outbox and inbox hops without a live network call?
**Response**:
"Standard OpenTelemetry HTTP instrumentation stops when an event is saved to a database outbox table.
I bridged this by capturing `Activity.Current?.Context` when creating the `OutboxMessage`, serializing the W3C `TraceParent` string into the outbox row.

When `OutboxProcessor` drains the message, it creates an `Activity` linked to that parent trace. When `InboxDispatcher` invokes the consumer, it extracts the `TraceParent` header and starts a child span. In Aspire Dashboard or Jaeger, the entire saga across all 4 modules appears as a single connected trace tree."
