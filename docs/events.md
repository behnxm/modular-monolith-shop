# Integration Event Catalog

## 1. Overview and Design Principles

In this modular monolith architecture, inter-module asynchronous communication is conducted exclusively via strongly-typed **Integration Events**. Modules are physically and logically segregated; no module may execute direct database writes or invoke state-mutating commands across boundary lines.

Every integration event adheres to strict design tenets:
- **Contracts Isolation**: Declared exclusively in the producing module's `.Contracts` assembly within namespace `Shop.Modules.<ModuleName>.Contracts.IntegrationEvents.V1`.
- **Immutability**: Declared as `sealed record` types with init-only properties and primary/JsonConstructors.
- **Explicit Versioning & Naming**: Annotated with `[IntegrationEventName("module.event-name.v#")]`.
- **Identity & Time**: Every event inherits from `IIntegrationEvent`, bearing a unique version-7 GUID (`Id`) and a UTC timestamp (`OccurredOn`).
- **Mechanical Boundary Enforcement**: Verified automatically via NetArchTest architecture rules and committed JSON schema snapshot tests.

---

## 2. Event Registry & Choreography Matrix

| Event Contract | Producer | Consumers | Business Trigger / Intent |
| :--- | :--- | :--- | :--- |
| `catalog.product-created.v1` | **Catalog** | **Inventory** | Initialises zero-stock inventory ledger entry for a newly created catalog SKU. |
| `ordering.order-placed.v1` | **Ordering** | **Inventory**, **Payments** | Kicks off the distributed checkout saga; triggers stock reservation and records pending payment. |
| `inventory.stock-reserved.v1` | **Inventory** | **Payments**, **Ordering** | Signals successful atomic reservation of all order line items; triggers payment charge attempt. |
| `inventory.stock-reservation-failed.v1` | **Inventory** | **Ordering** | Signals insufficient inventory; transitions order to `Cancelled` compensation state. |
| `payments.payment-succeeded.v1` | **Payments** | **Ordering** | Confirms successful funds capture; transitions order to `Confirmed`. |
| `payments.payment-failed.v1` | **Payments** | **Ordering** | Signals payment decline/failure; transitions order to `Cancelled`. |
| `payments.payment-refunded.v1` | **Payments** | *(Audit sink)* | Records that a captured charge was refunded due to an order cancellation race condition. |
| `ordering.order-confirmed.v1` | **Ordering** | **Inventory**, **Notifications** | Finalises saga; commits stock reservations and dispatches customer order confirmation notification. |
| `ordering.order-cancelled.v1` | **Ordering** | **Inventory**, **Payments**, **Notifications** | Compensates saga; releases reserved inventory, triggers payment refund if needed, and dispatches cancellation notice. |
| `notifications.notification-recorded.v1` | **Notifications** | *(Sink / Audit)* | Confirms asynchronous customer notification was successfully persisted into the dispatch ledger. |

---

## 3. Event Specifications & Wire Formats

### 3.1 `catalog.product-created.v1`
Published when an administrator registers a new active product in the catalog.

```json
{
  "id": "01940000-0000-7000-8000-000000000001",
  "occurredOn": "2026-11-01T12:00:00+00:00",
  "productId": "01940000-0000-7000-8000-000000000002",
  "sku": "PROD-SAMPLE-01",
  "name": "Sample Product"
}
```

- `id` (`Guid`): Version 7 unique identifier for the event message.
- `occurredOn` (`DateTimeOffset`): UTC timestamp when the product was created.
- `productId` (`Guid`): Immutable identifier of the newly catalogued product.
- `sku` (`string`): Alphanumeric merchant stock keeping unit.
- `name` (`string`): Human-readable product name.

---

### 3.2 `ordering.order-placed.v1`
Published when a customer submits a valid, price-verified order with an idempotency key.

```json
{
  "orderId": "01940000-0000-7000-8000-000000000010",
  "customerId": "cust_sample_123",
  "lines": [
    {
      "productId": "01940000-0000-7000-8000-000000000002",
      "quantity": 2
    }
  ],
  "totalAmount": 99.98,
  "currency": "USD",
  "id": "01940000-0000-7000-8000-000000000001",
  "occurredOn": "2026-11-01T12:00:00+00:00"
}
```

- `orderId` (`Guid`): Primary key of the newly submitted order.
- `customerId` (`string`): Subject identifier of the ordering customer.
- `lines` (`OrderLineSnapshot[]`): Itemized array of product IDs and ordered quantities.
- `totalAmount` (`decimal`): Grand total evaluated synchronously against Catalog pricing.
- `currency` (`string`): ISO 4217 3-letter currency code (`USD`).

---

### 3.3 `inventory.stock-reserved.v1`
Published when Inventory atomically checks and reserves sufficient available quantities for all lines.

```json
{
  "orderId": "01940000-0000-7000-8000-000000000010",
  "id": "01940000-0000-7000-8000-000000000001",
  "occurredOn": "2026-11-01T12:00:00+00:00"
}
```

- `orderId` (`Guid`): Order identifier for which inventory reservations were committed.

---

### 3.4 `inventory.stock-reservation-failed.v1`
Published when one or more products lack sufficient available stock to fulfil the order.

```json
{
  "orderId": "01940000-0000-7000-8000-000000000010",
  "reason": "Insufficient stock for product",
  "unavailableProductIds": [
    "01940000-0000-7000-8000-000000000002"
  ],
  "id": "01940000-0000-7000-8000-000000000001",
  "occurredOn": "2026-11-01T12:00:00+00:00"
}
```

- `orderId` (`Guid`): Order identifier that could not be satisfied.
- `reason` (`string`): Human-readable diagnostic description of the inventory shortage.
- `unavailableProductIds` (`Guid[]`): List of product IDs that caused the shortage.

---

### 3.5 `payments.payment-succeeded.v1`
Published when the payment gateway processes and approves the charge.

```json
{
  "orderId": "01940000-0000-7000-8000-000000000010",
  "paymentId": "01940000-0000-7000-8000-000000000020",
  "amount": 99.98,
  "currency": "USD",
  "id": "01940000-0000-7000-8000-000000000001",
  "occurredOn": "2026-11-01T12:00:00+00:00"
}
```

- `orderId` (`Guid`): Order identifier being paid.
- `paymentId` (`Guid`): Primary key of the successful payment transaction record.
- `amount` (`decimal`): Authorized monetary amount.
- `currency` (`string`): ISO 4217 currency code.

---

### 3.6 `payments.payment-failed.v1`
Published when the payment gateway rejects the charge or exceeds the approval limit ($10,000 threshold).

```json
{
  "orderId": "01940000-0000-7000-8000-000000000010",
  "reason": "Card declined by issuing bank",
  "id": "01940000-0000-7000-8000-000000000001",
  "occurredOn": "2026-11-01T12:00:00+00:00"
}
```

- `orderId` (`Guid`): Order identifier.
- `reason` (`string`): Gateway rejection or card error code explanation.

---

### 3.7 `payments.payment-refunded.v1`
Published when an asynchronous cancellation races against an in-flight payment, triggering an automated refund.

```json
{
  "orderId": "01940000-0000-7000-8000-000000000010",
  "paymentId": "01940000-0000-7000-8000-000000000020",
  "id": "01940000-0000-7000-8000-000000000001",
  "occurredOn": "2026-11-01T12:00:00+00:00"
}
```

- `orderId` (`Guid`): Order identifier.
- `paymentId` (`Guid`): Identifier of the payment transaction that was successfully reversed.

---

### 3.8 `ordering.order-confirmed.v1`
Published when both stock reservation and payment capture have concluded successfully.

```json
{
  "orderId": "01940000-0000-7000-8000-000000000010",
  "customerId": "cust_sample_123",
  "totalAmount": 99.98,
  "currency": "USD",
  "id": "01940000-0000-7000-8000-000000000001",
  "occurredOn": "2026-11-01T12:00:00+00:00"
}
```

- `orderId` (`Guid`): Order identifier.
- `customerId` (`string`): Customer ID.
- `totalAmount` (`decimal`): Final charged amount.
- `currency` (`string`): Currency code.

---

### 3.9 `ordering.order-cancelled.v1`
Published when the customer cancels an order or when compensation triggers due to stock or payment failure.

```json
{
  "orderId": "01940000-0000-7000-8000-000000000010",
  "customerId": "cust_sample_123",
  "reason": "Customer requested cancellation before shipment",
  "id": "01940000-0000-7000-8000-000000000001",
  "occurredOn": "2026-11-01T12:00:00+00:00"
}
```

- `orderId` (`Guid`): Order identifier.
- `customerId` (`string`): Customer ID.
- `reason` (`string`): Cancellation explanation.

---

### 3.10 `notifications.notification-recorded.v1`
Published when the Notifications module captures a customer communication record.

```json
{
  "id": "01940000-0000-7000-8000-000000000001",
  "occurredOn": "2026-11-01T12:00:00+00:00",
  "notificationId": "01940000-0000-7000-8000-000000000030",
  "customerId": "cust_sample_123",
  "kind": "OrderConfirmed"
}
```

- `id` (`Guid`): Event identifier.
- `occurredOn` (`DateTimeOffset`): Timestamp.
- `notificationId` (`Guid`): Primary key of the notification record.
- `customerId` (`string`): Recipient customer ID.
- `kind` (`string`): Notification classification (`OrderConfirmed` or `OrderCancelled`).

---

## 4. Schema Evolution & Versioning Rules

To ensure backward compatibility:
1. **Additive changes only**: New fields may be added as nullable properties or with sensible defaults.
2. **Never delete or rename existing fields**: Property removals or structural shifts break downstream consumers.
3. **Major version bumping**: If a breaking change is mandatory, create a new event record in a new version namespace (e.g., `Shop.Modules.Ordering.Contracts.IntegrationEvents.V2.OrderPlacedIntegrationEvent`) with attribute `[IntegrationEventName("ordering.order-placed.v2")]`.
4. **Snapshot enforcement**: Any change to event contracts is caught by `EventContractSnapshotTests`, ensuring accidental mutations cannot be committed unnoticed.
