# ADR 0001: Modular Monolith Architecture & Module Assembly Pairs

## Status
Accepted

## Context
When architecting modern enterprise systems, engineering teams frequently gravitate towards microservices prematurely. In my experience, microservices introduce substantial operational and distributed systems complexity: network latency, partial failure modes, distributed tracing overhead, eventual consistency synchronization, and intricate deployment pipelines. For the vast majority of systems, this overhead is incurred long before team topology or scaling requirements genuinely demand service decomposition.

Conversely, traditional monolithic architectures often degenerate into "big balls of mud" where boundaries are permeable, entities reference each other indiscriminately across domain lines, and database tables are conjoined by cross-cutting SQL joins and foreign keys.

I needed an architecture that combines the operational simplicity of a single deployable artifact with the architectural rigor and hard boundary enforcement of microservices.

## Decision
I chose to build the system as a disciplined **Modular Monolith** in .NET 10 (C# 14).

Each module in the monolith is structured as a pair of dedicated assemblies:
1. `Shop.Modules.<ModuleName>`: The internal implementation assembly containing Domain, Application use cases, Infrastructure, DbContext, and HTTP endpoints.
2. `Shop.Modules.<ModuleName>.Contracts`: The public contract assembly containing only immutable integration event records and minimal query interfaces.

Key tenets of this structure:
- **Assembly-Level Isolation**: No module implementation assembly is permitted to reference another module implementation assembly. The host (`Shop.Host`) references all implementation assemblies to wire them into the DI container and register HTTP routes.
- **Strictly Typed Public Contracts**: If Module A needs to communicate with Module B, it may only reference `Shop.Modules.B.Contracts`.
- **Mechanical Boundary Enforcement**: These boundaries are not merely conventions documented in an engineering wiki; they are enforced mechanically via:
  1. Internal-by-default C# type visibility.
  2. Automated architecture tests using NetArchTest (`tests/Shop.ArchitectureTests`).
  3. Database schema and role-level privilege segregation.

## Consequences
### Positive
- Single unified deployment unit (one container, one host process) minimizing cloud infrastructure costs and operational complexity.
- In-process call latency and reliability while preserving pristine domain boundaries.
- Seamless refactoring and compilation-time type safety across contracts.
- Any module can be extracted into an independent microservice in under an hour by replacing the in-process contract implementations with HTTP or gRPC clients and routing integration events over a message broker (e.g., Kafka or RabbitMQ).

### Negative
- Requires strict adherence to discipline: developers cannot simply write a quick direct method call or EF navigation property across modules.
- Slightly higher initial boilerplate due to explicit assembly pairs and outbox/inbox messaging.
