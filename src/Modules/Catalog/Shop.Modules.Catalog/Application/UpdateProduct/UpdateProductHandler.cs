using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Abstractions;
using Shop.Modules.Catalog.Domain;
using Shop.Modules.Catalog.Infrastructure;

namespace Shop.Modules.Catalog.Application.UpdateProduct;

internal sealed class UpdateProductHandler(CatalogDbContext dbContext)
{
    public async Task<Result> HandleAsync(
        Guid productId,
        UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var product = await dbContext.Products
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product is null)
        {
            return Result.Failure(CatalogErrors.ProductNotFound(productId));
        }

        var updateResult = product.Update(request.Name, request.Description, request.Price);
        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
