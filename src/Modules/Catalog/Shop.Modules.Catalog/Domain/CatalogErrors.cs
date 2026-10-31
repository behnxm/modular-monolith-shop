using Shop.BuildingBlocks.Abstractions;

namespace Shop.Modules.Catalog.Domain;

internal static class CatalogErrors
{
    public static Error ProductNotFound(Guid id) =>
        Error.NotFound("catalog.product_not_found", $"Product with identifier '{id}' was not found.");

    public static Error DuplicateSku(string sku) =>
        Error.Conflict("catalog.duplicate_sku", $"A product with SKU '{sku}' already exists.");

    public static Error InvalidSku(string message) =>
        Error.Validation("catalog.invalid_sku", message);

    public static Error InvalidPrice(string message) =>
        Error.Validation("catalog.invalid_price", message);

    public static Error InvalidName(string message) =>
        Error.Validation("catalog.invalid_name", message);

    public static Error InvalidDescription(string message) =>
        Error.Validation("catalog.invalid_description", message);

    public static readonly Error InvalidCursor =
        Error.Validation("catalog.invalid_cursor", "The provided pagination cursor is invalid or malformed.");

    public static readonly Error BatchLimitExceeded =
        Error.Validation("catalog.batch_limit_exceeded", "Cannot request more than 100 products in a single batch.");
}
