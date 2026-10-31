using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Abstractions;
using Shop.BuildingBlocks.Infrastructure.Outbox;
using Shop.Modules.Catalog.Contracts.IntegrationEvents.V1;
using Shop.Modules.Catalog.Domain;
using Shop.Modules.Catalog.Infrastructure;

namespace Shop.Modules.Catalog.Application.CreateProduct;

internal sealed class CreateProductHandler(
    CatalogDbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<Result<ProductResponse>> HandleAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var skuResult = Sku.Create(request.Sku);
        if (skuResult.IsFailure)
        {
            return Result<ProductResponse>.Failure(skuResult.Error);
        }

        var sku = skuResult.Value;
        var skuExists = await dbContext.Products
            .AnyAsync(p => p.Sku == sku, cancellationToken);

        if (skuExists)
        {
            return Result<ProductResponse>.Failure(CatalogErrors.DuplicateSku(sku.Value));
        }

        var productResult = Product.Create(
            request.Sku,
            request.Name,
            request.Description,
            request.Price,
            timeProvider);

        if (productResult.IsFailure)
        {
            return Result<ProductResponse>.Failure(productResult.Error);
        }

        var product = productResult.Value;
        var now = timeProvider.GetUtcNow();

        var integrationEvent = new ProductCreatedIntegrationEvent(
            Guid.CreateVersion7(now),
            now,
            product.Id,
            product.Sku.Value,
            product.Name);

        // Atomic commit: outbox row and entity change are saved in the same transaction
        dbContext.Products.Add(product);
        dbContext.Enqueue(integrationEvent, timeProvider);

        await dbContext.SaveChangesAsync(cancellationToken);

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
