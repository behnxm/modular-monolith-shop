# ADR 0002: Module Visibility, Internal by Default, and Design-Time DbContext Factories

## Status
Accepted

## Context
In C#, types default to `internal` only within specific scopes, and classes default to `internal` if unqualified, but developers frequently mark classes `public` out of habit. In a modular monolith hosted in a single solution, if classes in an implementation assembly are `public`, developers in other modules (or the host) can easily take direct dependencies on internal domain aggregates, handlers, or DbContext instances.

Furthermore, Entity Framework Core CLI tools (`dotnet ef migrations add`) need to discover and instantiate `DbContext` classes at design time without running the entire application host or requiring the DbContext to be public.

## Decision
I established the following encapsulation and visibility policies:
1. **Internal by Default**: Every class, record, and interface inside `Shop.Modules.<ModuleName>` is explicitly declared `internal`, with exactly one exception: the module entry point implementing `IModule` (e.g., `CatalogModule : IModule`).
2. **Architecture Test Enforcement**: An automated test (`A4_ModuleImplementation_OnlyExposes_IModuleClass_Publicly`) reflects over all types in every implementation assembly and asserts that only the `IModule` class (and permitted design-time factories) are exported publicly.
3. **Design-Time DbContext Factories**: To allow EF Core tools to scaffold migrations without making the DbContext `public` or requiring complex host boot routines, each module implements an `IDesignTimeDbContextFactory<TContext>`. By placing this factory inside the module assembly, EF tools can discover it directly, passing a dummy or local connection string to generate migration snapshots cleanly.

## Consequences
### Positive
- Total encapsulation: foreign modules cannot compile code that touches domain entities, repositories, handlers, or DbContexts of another module.
- EF Core migrations can be added and managed per module from the CLI (`dotnet ef migrations add ... --project ...`) in total isolation.

### Negative
- Developers must explicitly declare `IDesignTimeDbContextFactory` in each module. I automated this via the scaffolding script `scripts/new-module.sh`.
