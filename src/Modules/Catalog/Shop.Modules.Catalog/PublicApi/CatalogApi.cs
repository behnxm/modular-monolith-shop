using Microsoft.EntityFrameworkCore;
using Shop.Modules.Catalog.Contracts;
using Shop.Modules.Catalog.Infrastructure;

namespace Shop.Modules.Catalog.PublicApi;

internal sealed class CatalogApi(CatalogDbContext dbContext) : ICatalogApi
{
    private const int MaxBatchSize = 100;

    public async Task<IReadOnlyList<ProductSnapshot>> GetProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        if (productIds.Count == 0)
        {
            return Array.Empty<ProductSnapshot>();
        }

        if (productIds.Count > MaxBatchSize)
        {
            throw new ArgumentException(
                $"Cannot query more than {MaxBatchSize} products in a single call.",
                nameof(productIds));
        }

        var distinctIds = productIds.Distinct().ToList();

        var products = await dbContext.Products
            .AsNoTracking()
            .Where(p => distinctIds.Contains(p.Id))
            .Select(p => new ProductSnapshot(
                p.Id,
                p.Sku.Value,
                p.Name,
                p.Price,
                p.Currency,
                p.IsActive))
            .ToListAsync(cancellationToken);

        return products;
    }
}
