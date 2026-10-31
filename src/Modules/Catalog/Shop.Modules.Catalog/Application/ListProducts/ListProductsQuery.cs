namespace Shop.Modules.Catalog.Application.ListProducts;

internal sealed record ListProductsQuery(
    string? Cursor = null,
    int Limit = 25,
    string? Search = null);

internal sealed record PagedProductsResponse(
    IReadOnlyList<ProductResponse> Items,
    string? NextCursor);
