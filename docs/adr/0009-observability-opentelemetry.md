# ADR 0009: Observability: OpenTelemetry Tracing, Metrics, and Structured Logging

## Status
Accepted

## Context
Diagnosing issues in an asynchronous, event-driven modular monolith requires unified distributed tracing. When an order transitions from `ordering` to `inventory`, `payments`, and `notifications` across transactional outboxes and inboxes, traditional synchronous logs fail to capture the causality of the saga.

I needed standard, vendor-neutral observability that tracks messages across outbox/inbox hops and exports telemetry to modern APM systems (e.g., Aspire Dashboard, Jaeger, Prometheus, Datadog).

## Decision
I standardized on **OpenTelemetry (.NET)**:

1. **Activity Source & Meters**:
   - `ShopTelemetry.ServiceName = "Shop.Host"`
   - `ShopTelemetry.ActivitySource = new ActivitySource("Shop.Host")`
   - `ShopTelemetry.Meter = new Meter("Shop.Host")`
2. **Trace Propagation Across Outbox/Inbox**:
   - When an event is serialized into an `OutboxMessage`, the current `ActivityContext` (TraceId, SpanId, TraceFlags) is captured in `TraceParent` and stored with the message.
   - When `OutboxProcessor` publishes the event, it creates an activity with `ActivityKind.Producer` and links to the original trace.
   - When `InboxDispatcher` receives the envelope, it extracts `TraceParent` and starts an `ActivityKind.Consumer` span as a child of the outbox span.
   - This provides end-to-end distributed trace continuity in trace viewers, exactly as if the events traveled over Kafka or RabbitMQ!
3. **Structured Metrics**:
   - Outbox processing latency, batch sizes, retry counts, and dead-letter counts are instrumented via `System.Diagnostics.Metrics`.
4. **OTLP Exporter**:
   - Configured via standard environment variable `OTEL_EXPORTER_OTLP_ENDPOINT` (pointing to Aspire Dashboard or OpenTelemetry Collector).

## Consequences
### Positive
- Unified end-to-end traces across all 5 modules and asynchronous outbox/inbox hops.
- Seamless local visualization using `compose.yaml` with the .NET Aspire Dashboard profile.
- Zero vendor lock-in.

### Negative
- Telemetry headers add minor byte overhead to outbox message records.
