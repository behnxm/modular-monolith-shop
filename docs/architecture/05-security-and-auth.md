# 05. Security and Authorization Architecture

## 1. Threat Model & Mitigations

| Threat Vector | Risk Description | Architectural Mitigation |
| :--- | :--- | :--- |
| **BOLA / IDOR** | Attacker tampers with URL IDs to read or cancel other customers' orders. | Handlers query database with compound filter `(Id == orderId && CustomerId == currentUserId)`. Returns HTTP 404 on mismatch. |
| **Price Tampering** | Malicious client submits manipulated unit prices in the POST request body. | Incoming prices are ignored. Order total is computed exclusively from point-in-time snapshots fetched from `ICatalogApi`. |
| **Duplicate Orders** | Network timeouts cause clients to replay order POST requests. | Mandatory `Idempotency-Key` header with SHA-256 payload verification. Cached replay returns HTTP 202; conflicting payload returns HTTP 409. |
| **Payload Bombing (DoS)** | Attacker streams huge JSON payloads or deep JSON objects to exhaust RAM. | Kestrel max body limit set to 64 KB; JSON max depth capped at 64; unmapped member handling set to `Disallow`. |
| **Cross-Schema Injection** | SQL vulnerability in one module used to query sensitive data in another module. | Database role segregation: `catalog_app` has zero permissions on other schemas. Enforced at the PostgreSQL engine level. |
| **Privilege Escalation** | Compromised web process attempts to write root system files or install tools. | Docker container drops all Linux capabilities (`cap_drop: ALL`), runs as non-root user (`USER $APP_UID`), with read-only root filesystem. |

---

## 2. Policy-Based Authorization Matrix

Authentication is enforced via JWT bearer tokens signed with ECDSA (P-256).

| Endpoint | Method | Required Scope / Policy | Anonymous Access | BOLA Protected |
| :--- | :--- | :--- | :--- | :--- |
| `/health/live` | GET | None | **Yes** | No |
| `/health/ready` | GET | None | **Yes** | No |
| `/health/modules` | GET | None | **Yes** | No |
| `/openapi/v1.json` | GET | None | **Yes** | No |
| `/dev/token` | POST | None (Rate limited: 10/min) | **Yes** | No |
| `/api/v1/catalog/products` | GET | `shop.customer` | No (401) | No |
| `/api/v1/catalog/products/{id}` | GET | `shop.customer` | No (401) | No |
| `/api/v1/catalog/products` | POST | `shop.admin` | No (401/403) | No |
| `/api/v1/catalog/products/{id}` | PUT | `shop.admin` | No (401/403) | No |
| `/api/v1/catalog/products/{id}` | DELETE | `shop.admin` | No (401/403) | No |
| `/api/v1/inventory/stock/{id}` | PUT | `shop.admin` | No (401/403) | No |
| `/api/v1/inventory/stock/{id}` | GET | `shop.customer` | No (401) | No |
| `/api/v1/inventory/stock` | GET | `shop.customer` | No (401) | No |
| `/api/v1/ordering/orders` | POST | `shop.customer` | No (401) | No |
| `/api/v1/ordering/orders/{id}` | GET | `shop.customer` | No (401) | **Yes (404 on foreign)** |
| `/api/v1/ordering/orders` | GET | `shop.customer` | No (401) | **Yes (Customer only)** |
| `/api/v1/ordering/orders/admin` | GET | `shop.admin` | No (401/403) | No |
| `/api/v1/ordering/orders/{id}/cancel` | POST | `shop.customer` | No (401) | **Yes (404 on foreign)** |
| `/api/v1/payments/{orderId}` | GET | `shop.admin` | No (401/403) | No |
| `/api/v1/notifications` | GET | `shop.customer` | No (401) | **Yes (Customer only)** |

---

## 3. Broken Object Level Authorization (BOLA) Implementation

In `GetOrderHandler.cs`:
```csharp
var order = await dbContext.Orders
    .AsNoTracking()
    .Include(o => o.Lines)
    .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

if (order is null)
{
    return OrderingErrors.OrderNotFound(orderId); // 404
}

// BOLA Check: If order belongs to another customer, return 404 rather than 403
if (order.CustomerId != currentUser.UserId)
{
    return OrderingErrors.OrderNotFound(orderId); // 404
}
```

Returning `404 Not Found` rather than `403 Forbidden` prevents attackers from determining whether an arbitrary order ID exists in the system.
