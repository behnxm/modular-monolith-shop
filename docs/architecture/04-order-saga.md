# 04. Order Saga Choreography and Compensations

## 1. The Distributed Saga Lifecycle

The e-commerce order checkout lifecycle spans four modules (`ordering`, `inventory`, `payments`, `notifications`). Because each module has a dedicated PostgreSQL schema and connection pool, cross-module operations are coordinated via an asynchronous saga with explicit compensations.

---

## 2. Happy Path Flowchart

```mermaid
flowchart TD
    Start([Customer places order]) --> PlaceOrder["ordering: PlaceOrderHandler (Pending)"]
    PlaceOrder --> EvtOrderPlaced["Event: ordering.order-placed.v1"]
    
    EvtOrderPlaced --> InvReserve["inventory: OrderPlacedHandler"]
    EvtOrderPlaced --> PayTrack["payments: OrderPlacedHandler (Records Pending)"]

    InvReserve --> AtomicCheck{"Stock Available?"}
    AtomicCheck -- Yes --> EvtStockReserved["Event: inventory.stock-reserved.v1"]
    
    EvtStockReserved --> PayCharge["payments: StockReservedHandler (Charges Gateway)"]
    EvtStockReserved --> OrderStatusUpdate["ordering: StockReservedHandler (Status: StockReserved)"]

    PayCharge --> GatewayCall{"Payment Approved?"}
    GatewayCall -- Yes --> EvtPaySuccess["Event: payments.payment-succeeded.v1"]

    EvtPaySuccess --> OrderConfirm["ordering: PaymentSucceededHandler (Status: Confirmed)"]
    OrderConfirm --> EvtOrderConfirmed["Event: ordering.order-confirmed.v1"]

    EvtOrderConfirmed --> InvCommit["inventory: OrderConfirmedHandler (Commit Stock)"]
    EvtOrderConfirmed --> NotifConfirm["notifications: OrderConfirmedHandler (Send Email)"]
```

---

## 3. Failure & Compensation Branches

### 3.1 Insufficient Inventory Compensation
When available stock is less than requested:
```mermaid
flowchart TD
    InvShortage["inventory: Insufficient stock"] --> EvtShortage["Event: inventory.stock-reservation-failed.v1"]
    EvtShortage --> OrderCancel["ordering: StockReservationFailedHandler (Status: Cancelled)"]
    OrderCancel --> EvtOrderCancelled["Event: ordering.order-cancelled.v1"]
    EvtOrderCancelled --> NotifCancel["notifications: OrderCancelledHandler (Notify Customer)"]
```

### 3.2 Payment Decline & Inventory Release
When payment gateway declines authorization (e.g., amount >= $10,000 threshold or card declined):
```mermaid
flowchart TD
    PayDeclined["payments: Payment declined"] --> EvtPayFailed["Event: payments.payment-failed.v1"]
    EvtPayFailed --> OrderCancel["ordering: PaymentFailedHandler (Status: Cancelled)"]
    OrderCancel --> EvtOrderCancelled["Event: ordering.order-cancelled.v1"]
    EvtOrderCancelled --> InvRelease["inventory: OrderCancelledHandler (Release Reserved Stock)"]
    EvtOrderCancelled --> NotifCancel["notifications: OrderCancelledHandler (Notify Customer)"]
```

### 3.3 Racing Customer Cancellation with Automatic Refund
If a customer cancels an order concurrently with payment processing:
```mermaid
flowchart TD
    CustCancel["Customer POST /cancel"] --> OrderCancel["ordering: Status: Cancelled"]
    OrderCancel --> EvtOrderCancelled["Event: ordering.order-cancelled.v1"]
    EvtOrderCancelled --> PayHandler["payments: OrderCancelledHandler"]
    PayHandler --> CheckPaid{"Payment already captured?"}
    CheckPaid -- Yes --> GatewayRefund["payments: IPaymentGateway.RefundAsync()"]
    GatewayRefund --> EvtRefunded["Event: payments.payment-refunded.v1"]
    CheckPaid -- No --> PayAbort["payments: Mark payment attempt Cancelled"]
```

---

## 4. Saga Invariants Verified by Automated Integration Tests

In `tests/Shop.IntegrationTests/OrderSagaIntegrationTests.cs`, the test suite verifies:
1. **Happy Path End-to-End**: Order transitions from `Pending` -> `StockReserved` -> `Confirmed`. Stock is committed. Notifications are recorded.
2. **Stock Shortage**: Order transitions to `Cancelled`. Zero stock committed.
3. **Payment Decline Compensation**: Stock is reserved, payment fails, order transitions to `Cancelled`, reserved stock is released back to available inventory.
4. **Racing Cancellation**: Customer cancels before confirmation. Completed payment is automatically refunded.
5. **Idempotent Replay**: Duplicate order submissions return the existing order without re-triggering saga events.
