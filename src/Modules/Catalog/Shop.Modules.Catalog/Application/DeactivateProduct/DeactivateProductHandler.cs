using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Abstractions;
using Shop.Modules.Catalog.Domain;
using Shop.Modules.Catalog.Infrastructure;

namespace Shop.Modules.Catalog.Application.DeactivateProduct;

internal sealed class DeactivateProductHandler(CatalogDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product is null)
        {
            return Result.Failure(CatalogErrors.ProductNotFound(productId));
        }

        product.Deactivate();
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
