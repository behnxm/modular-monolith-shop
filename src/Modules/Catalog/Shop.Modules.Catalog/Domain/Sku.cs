using System.Text.RegularExpressions;
using Shop.BuildingBlocks.Abstractions;

namespace Shop.Modules.Catalog.Domain;

internal sealed record Sku
{
    private static readonly Regex SkuRegex = new("^[A-Z0-9-]+$", RegexOptions.Compiled);

    public string Value { get; }

    private Sku(string value)
    {
        Value = value;
    }

    public static Result<Sku> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<Sku>.Failure(CatalogErrors.InvalidSku("SKU must not be empty."));
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length is < 3 or > 32)
        {
            return Result<Sku>.Failure(CatalogErrors.InvalidSku("SKU must be between 3 and 32 characters long."));
        }

        if (!SkuRegex.IsMatch(normalized))
        {
            return Result<Sku>.Failure(CatalogErrors.InvalidSku("SKU must contain only uppercase letters, digits, and hyphens."));
        }

        return Result<Sku>.Success(new Sku(normalized));
    }

    public override string ToString() => Value;
}
