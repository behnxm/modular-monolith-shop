# ADR 0010: Health Checks and Module Lifecycle Validation

## Status
Accepted

## Context
In containerized orchestrations (Kubernetes, AWS ECS, Docker Compose), orchestrators need distinct probes to differentiate between:
1. **Liveness**: Is the process alive and responsive, or is it deadlocked and needing a restart?
2. **Readiness**: Is the process ready to accept incoming traffic (e.g., are PostgreSQL connections healthy)?
3. **Module Operational Health**: Are background outbox processors keeping up, or are events accumulating or dead-lettering?

Furthermore, when modules can be selectively enabled or disabled via configuration (`Modules:Enabled`), enabling Module A without its required dependency Module B must fail fast at startup rather than throwing cryptic runtime `NullReferenceException`s during customer requests.

## Decision
I implemented a tiered health and validation strategy:

1. **Standard Health Endpoints**:
   - `/health/live`: Lightweight probe returning `200 OK ("Healthy")` without database dependencies. Used for Kubernetes liveness probes.
   - `/health/ready`: Checks PostgreSQL database connectivity for all enabled modules. Used for Kubernetes readiness probes.
   - `/health/modules`: Dedicated diagnostic endpoint returning structured JSON detailing the outbox processing state of every enabled module (processed count, pending count, dead-letter count).
2. **Outbox Health Checks**:
   - Each module registers an `OutboxHealthCheck<TContext>` verifying that the outbox processor is actively draining messages and has not encountered consecutive unhandled loop failures.
3. **Module Dependency Validator (`ModuleValidator`)**:
   - During host bootstrap, before services or endpoints are registered, `ModuleValidator.ValidateDependencies` evaluates all enabled modules against their `RequiredModules` declarations.
   - If a dependency is missing (e.g. `notifications` is enabled but its required dependency `ordering` is omitted from `Modules:Enabled`), the validator immediately throws an `InvalidOperationException` with an actionable error message, terminating startup before accepting traffic.

## Consequences
### Positive
- Production orchestrators can safely manage container restarts and traffic routing.
- Early detection of configuration errors preventing broken runtime states.
- High observability into module message backlogs.

### Negative
- Health check endpoints must be mapped with `.AllowAnonymous()` to allow load balancers to probe without JWT authentication.
