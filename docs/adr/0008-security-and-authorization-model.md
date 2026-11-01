# ADR 0008: Security and Authorization Model

## Status
Accepted

## Context
A production-grade modular monolith requires robust authentication and authorization that scales from development to production without hardcoding test compromises into production pipelines.

Key risks to mitigate:
1. **Broken Object Level Authorization (BOLA / IDOR)**: Customers attempting to view or cancel other customers' orders by guessing or iterating GUIDs.
2. **Role-Based Attribute Proliferation**: Scattered `[Authorize(Roles="Admin")]` strings throughout the codebase that resist refactoring and auditing.
3. **Insecure Development Backdoors**: Shipping mock authentication handlers or unauthenticated bypasses into production images.

## Decision
I implemented a defense-in-depth security model:

1. **Policy-Based Authorization**:
   - Hardcoded role strings are strictly forbidden and enforced by architecture test `A10_NoRoleLiterals_OrAuthorizeRoleAttributes`.
   - Authorization relies exclusively on strongly-typed ASP.NET Core policies:
     - `shop.customer`: Requires claim `scope` (or `scp`) containing `shop.customer`.
     - `shop.admin`: Requires claim `scope` containing `shop.admin`.
   - Fallback policy mandates authentication for all endpoints by default; public endpoints must explicitly call `.AllowAnonymous()`.

2. **BOLA Protection**:
   - In all customer-facing endpoints (e.g. `GET /api/v1/ordering/orders/{id}`, `POST /api/v1/ordering/orders/{id}/cancel`), the query filters directly on `(order.Id == id && order.CustomerId == currentUser.UserId)`.
   - If an order exists but belongs to another customer, the handler returns **HTTP 404 Not Found**, never HTTP 403 Forbidden. This prevents attackers from enumerating valid order IDs through differential status code analysis.

3. **Development Token Issuer (`DevTokenIssuer`)**:
   - For local development, automated testing, and developer demonstrations without spinning up Keycloak or Auth0, a local ECDSA-signed JWT issuer is provided at `POST /dev/token`.
   - **Production Hardening**: `DevTokenIssuer.ValidateConfiguration` checks `IHostEnvironment.IsProduction()`. If `DevIssuer:Enabled` is set in production, the application immediately throws a `FatalSecurityException` and crashes at startup.

4. **Security Headers & Transport**:
   - `SecurityHeadersMiddleware` automatically injects:
     - `X-Content-Type-Options: nosniff`
     - `X-Frame-Options: DENY`
     - `Referrer-Policy: no-referrer`
     - `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`
     - `Strict-Transport-Security` (non-development)
     - `Cache-Control: no-store` on API routes
     - Removes `Server` header.

## Consequences
### Positive
- Zero information leakage on customer resource access.
- Fully automated authorization matrix testing in CI.
- Safe, production-gated development token generator.

### Negative
- Developers must supply bearer tokens when testing endpoints in Swagger/Scalar (conveniently generated via `requests.http` or `/dev/token`).
