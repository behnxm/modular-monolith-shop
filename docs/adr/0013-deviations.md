# ADR 0013: Architectural Deviations, Trade-Offs, and Scope Decisions

## Status
Accepted

## Context
When executing written architectural specifications, real-world engineering trade-offs arise between theoretical purity, operational maintainability, interview clarity, and reproducibility. I explicitly document all deliberate trade-offs, deviations, and scoping decisions here to provide full transparency into my architectural choices.

## Documented Deviations and Rationale

### 1. In-Process Event Transport vs External Message Broker
- **Decision**: The system utilizes an in-process, channel-backed transport (`InProcessEventTransport`) instead of running an external Kafka or RabbitMQ cluster.
- **Rationale**: For an in-process modular monolith, routing events out of process over a network broker adds substantial latency, serialization overhead, operational failure points, and developer machine resource bloat for zero architectural benefit. The transport sits strictly behind the `IIntegrationEventTransport` interface; extracting any module to an external broker requires only swapping the implementation of this interface without altering module contracts or use cases.

### 2. Hand-Crafted Outbox and Inbox vs MassTransit / Wolverine
- **Decision**: All transactional outbox and inbox mechanisms are hand-crafted directly using EF Core and PostgreSQL's native `FOR UPDATE SKIP LOCKED`.
- **Rationale**: Third-party messaging frameworks (MassTransit, Wolverine, Rebus) introduce complex configuration DSLs, opinionated conventions, license churn, and heavy dependency trees. Hand-crafting these patterns allows precise control over query hints (`SKIP LOCKED`), dead-letter semantics, and transactional guarantees, serving as a transparent reference implementation for technical interviews.

### 3. Deliberate Omission of In-Module Domain Events
- **Decision**: Modules do not implement a secondary "in-memory domain events" layer within their internal aggregates (e.g. `aggregate.AddDomainEvent(...)` dispatched prior to `SaveChanges`). Instead, use cases explicitly persist state and publish integration events to the outbox.
- **Rationale**: In my experience, mixing in-memory domain events with inter-module integration events frequently confuses junior and mid-level developers, often leading to unintended side effects, double publishing, and ordering bugs. In a modular monolith where aggregates are well-scoped and use cases are small, explicit outbox publishing keeps execution paths transparent and debuggable. If aggregate complexity grows to warrant complex in-aggregate event sourcing or side-effect decoupled validation, a separate domain event dispatcher can be introduced within the module without impacting foreign modules.

### 4. Fake Payment Gateway Threshold Simulation
- **Decision**: The `payments` module integrates with an in-memory `FakePaymentGateway` with an automated $10,000 threshold (approving charges under $10,000 and declining charges >= $10,000) rather than calling Stripe or PayPal APIs.
- **Rationale**: Enables deterministic, repeatable integration tests and technical interview walkthroughs without requiring external API keys, sandbox credentials, or internet access during automated CI test suites.

### 5. UUID Version 7 (Guid.CreateVersion7)
- **Decision**: Standardized on UUID v7 for all primary keys, aggregate identifiers, and integration event IDs.
- **Rationale**: .NET 9+ and .NET 10 provide native support for UUID v7 via `Guid.CreateVersion7()`. Unlike random UUID v4, v7 is monotonically time-ordered, preventing B-tree page splits and index fragmentation in PostgreSQL while retaining the distributed generation benefits of GUIDs.

### 6. Scope Discipline (Non-Goals)
The following are deliberate architectural non-goals:
- **No UI**: The monolith is an API-first backend reference implementation.
- **No Real Email Service**: The notifications module serves as an asynchronous event sink and ledger.
- **No Distributed Rate Limiting**: Fixed-window in-memory rate limiting is sufficient for a single host deployment.
- **No Distributed Lock Manager**: Handled via database row-level locking (`FOR UPDATE SKIP LOCKED`).
