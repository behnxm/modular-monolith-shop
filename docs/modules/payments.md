# Payments Module

## What it owns
The **Payments** module manages payment transactions and gateway communications:
- Payment attempts (`PaymentAttempt`: `Id`, `OrderId`, `Amount`, `Currency`, `Status`, `CreatedAtUtc`, `CompletedAtUtc`, `TransactionReference`, `FailureReason`)
- Payment status state machine (`Pending`, `Succeeded`, `Failed`, `Refunded`)
- Payment gateway abstraction (`IPaymentGateway`, `FakePaymentGateway`)
- The `payments` schema in PostgreSQL (`payments.payment_attempts`, `payments.outbox_messages`, `payments.inbox_messages`).

## Public API
The module exposes no synchronous query API. All interactions with foreign modules are handled asynchronously via integration events.

## Events it publishes
- `payments.payment-succeeded.v1` (`PaymentSucceededIntegrationEvent`): Published when payment is successfully authorized and captured. Consumed by `ordering`.
- `payments.payment-failed.v1` (`PaymentFailedIntegrationEvent`): Published when the payment gateway declines the charge. Consumed by `ordering`.
- `payments.payment-refunded.v1` (`PaymentRefundedIntegrationEvent`): Published when a previously captured payment is refunded due to a race with order cancellation.

## Events it consumes
- `ordering.order-placed.v1`: Consumed to record a `Pending` payment attempt and store the expected charge amount.
- `inventory.stock-reserved.v1`: Consumed to execute the payment charge through the payment gateway once stock is guaranteed.
- `ordering.order-cancelled.v1`: Consumed to evaluate payment state. If payment was already completed, it issues a refund via the gateway and publishes `PaymentRefunded`. If payment was still pending, it marks the attempt cancelled.

## Endpoints and policies
All routes are grouped under `/api/v1/payments`:
- `GET /api/v1/payments/{orderId}`: Inspects the payment attempt for a given order. Policy: `shop.admin`, Rate limit: `read`. Returns `200 OK` or `404 NotFound`.

## Rules and invariants
1. **Charge Only After Stock Reserved**: Payments never charges the customer's card until `inventory` publishes `StockReserved`. This avoids charging cards for orders that cannot be fulfilled.
2. **Deterministic Gateway Threshold**: In the test and demonstration environment, `FakePaymentGateway` approves all charges under $10,000.00 and declines charges of $10,000.00 or greater, allowing deterministic test coverage of success and decline saga branches.
3. **Refund on Cancellation Race**: If a customer cancels an order immediately after stock is reserved and the payment charge completes in parallel, the `OrderCancelledIntegrationEventHandler` catches the completed payment and issues an immediate automated refund.

## How it is tested
- **Domain Tests** (`PaymentsDomainTests.cs`): Verifies payment status transitions and refund rules.
- **Integration Tests** (`PaymentsIntegrationTests.cs`): Verifies `OrderPlaced` tracking, `StockReserved` charging, and outbox event persistence.
- **System Saga Tests** (`OrderSagaIntegrationTests.cs`): Verifies declined payments trigger compensation, and racing cancellations trigger automated refunds.
