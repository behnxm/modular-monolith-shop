# Notifications Module

## What it owns
The **Notifications** module serves as the asynchronous communication sink and audit ledger for customer communications:
- Customer notifications (`Notification`: `Id`, `CustomerId`, `Kind`: `OrderConfirmed` / `OrderCancelled`, `Subject`, `Body`, `CreatedAtUtc`)
- The `notifications` schema in PostgreSQL (`notifications.notifications`, `notifications.outbox_messages`, `notifications.inbox_messages`).

## Public API
The module exposes no synchronous query API.

## Events it publishes
- `notifications.notification-recorded.v1` (`NotificationRecordedIntegrationEvent`): Published via the outbox when a notification record is committed to disk.

## Events it consumes
- `ordering.order-confirmed.v1`: Consumed to record an "Order Confirmed" email/notification payload for the customer.
- `ordering.order-cancelled.v1`: Consumed to record an "Order Cancelled" notification payload explaining the cancellation reason to the customer.

## Endpoints and policies
All routes are grouped under `/api/v1/notifications`:
- `GET /api/v1/notifications`: Lists all recorded notifications for the authenticated customer. Policy: `shop.customer`, Rate limit: `read`. Returns `200 OK` with `IReadOnlyList<NotificationDto>` (BOLA protected: filters on `currentUser.UserId`).

## Rules and invariants
1. **Pure Asynchronous Sink**: Notifications are created exclusively in response to terminal order events. The module never mutates upstream state.
2. **Customer Privacy Isolation**: Customers can only view their own notifications. Queries automatically filter by the calling token's `sub` claim.
3. **Template Formatting**: Generates deterministic notification text containing order numbers, amounts, and cancellation reasons.

## How it is tested
- **Domain Tests** (`NotificationsDomainTests.cs`): Verifies notification creation and template text formatting.
- **Integration Tests** (`NotificationsIntegrationTests.cs`): Verifies `OrderConfirmed` and `OrderCancelled` event handlers persist notifications into the database and emit outbox records.
- **System Saga Tests** (`OrderSagaIntegrationTests.cs`): Verifies notifications are recorded for both completed and cancelled orders.
