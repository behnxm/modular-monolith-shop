# ADR 0007: Idempotent Order Placement with Request Payload Hashing

## Status
Accepted

## Context
In mobile and web e-commerce applications, network timeouts and retries frequently lead to duplicate submissions of order requests. If a client submits `POST /api/v1/ordering/orders`, experiences a network dropout before receiving the HTTP response, and automatically retries, the backend might create two identical orders and charge the customer twice.

Standard idempotency implementations often store only the idempotency key and return the original response. However, this exposes a subtle bug: if a client maliciously or accidentally sends a different request body with an already-used idempotency key, returning the original response would silently mask an invalid or conflicting operation.

## Decision
I designed an **Idempotency Pattern with SHA-256 Request Payload Hashing**:

1. **Mandatory Header**:
   - `POST /api/v1/ordering/orders` requires an `Idempotency-Key` header (max 64 characters).
2. **Canonical Payload Hashing**:
   - Before executing the use case, `PlaceOrderHandler` computes a deterministic SHA-256 hash of the order's itemized lines (sorted by ProductId and Quantity).
3. **Database Ledger (`ordering.idempotency_records`)**:
   - Stores `(idempotency_key, customer_id, request_hash, order_id, created_at)`.
   - The primary key is `(idempotency_key, customer_id)`.
4. **Conflict vs Replay Logic**:
   - **Identical Replay**: If the idempotency key exists and the incoming `request_hash` matches the stored hash, the handler bypasses saga creation, queries the existing order, appends the header `Idempotent-Replayed: true`, and returns HTTP 202 Accepted.
   - **Payload Conflict**: If the idempotency key exists but the `request_hash` differs, the handler rejects the request with HTTP 409 Conflict (`ordering.idempotency_conflict`), informing the client that the key was already used for a different payload.
5. **Atomic Persistence**:
   - The new order, outbox event, and idempotency record are persisted within the same database transaction.

## Consequences
### Positive
- Prevents duplicate orders, phantom charges, and double inventory deductions under flaky network conditions.
- Cryptographically protects against key reuse with differing payloads.
- Verified by automated tests in `OrderSagaIntegrationTests.cs`.

### Negative
- Minor compute overhead for computing SHA-256 hashes (negligible for JSON payloads).
- Storage footprint for idempotency records.
