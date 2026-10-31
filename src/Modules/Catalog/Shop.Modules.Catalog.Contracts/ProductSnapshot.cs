namespace Shop.Modules.Catalog.Contracts;

public sealed record ProductSnapshot(
    Guid Id,
    string Sku,
    string Name,
    decimal UnitPrice,
    string Currency,
    bool IsActive);
