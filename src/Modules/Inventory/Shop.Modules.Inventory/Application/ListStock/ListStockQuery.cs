namespace Shop.Modules.Inventory.Application.ListStock;

internal sealed record ListStockQuery(int Limit = 25, string? Cursor = null);

internal sealed record PagedStockResponse(
    IReadOnlyList<StockResponse> Items,
    string? NextCursor,
    bool HasNextPage);
