using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Abstractions;
using Shop.Modules.Catalog.Domain;
using Shop.Modules.Catalog.Infrastructure;

namespace Shop.Modules.Catalog.Application.GetProduct;

internal sealed class GetProductHandler(CatalogDbContext dbContext)
{
    public async Task<Result<ProductResponse>> HandleAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product is null)
        {
            return Result<ProductResponse>.Failure(CatalogErrors.ProductNotFound(productId));
        }

        var response = new ProductResponse(
            product.Id,
            product.Sku.Value,
            product.Name,
            product.Description,
            product.Price,
            product.Currency,
            product.IsActive,
            product.CreatedAt);

        return Result<ProductResponse>.Success(response);
    }
}
