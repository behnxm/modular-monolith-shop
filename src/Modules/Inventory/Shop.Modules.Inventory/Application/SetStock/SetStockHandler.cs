using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Abstractions;
using Shop.Modules.Inventory.Domain;
using Shop.Modules.Inventory.Infrastructure;

namespace Shop.Modules.Inventory.Application.SetStock;

internal sealed class SetStockHandler(InventoryDbContext dbContext)
{
    public async Task<Result<StockResponse>> HandleAsync(
        Guid productId,
        SetStockRequest request,
        CancellationToken cancellationToken)
    {
        if (request.OnHand < 0)
        {
            return Result<StockResponse>.Failure(InventoryErrors.NegativeOnHandStock);
        }

        var stockItem = await dbContext.StockItems
            .FirstOrDefaultAsync(s => s.ProductId == productId, cancellationToken);

        if (stockItem is null)
        {
            stockItem = StockItem.Create(productId, request.OnHand);
            dbContext.StockItems.Add(stockItem);
        }
        else
        {
            var updateResult = stockItem.SetOnHand(request.OnHand);
            if (updateResult.IsFailure)
            {
                return Result<StockResponse>.Failure(updateResult.Error);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<StockResponse>.Success(
            new StockResponse(
                stockItem.ProductId,
                stockItem.OnHand,
                stockItem.Reserved,
                stockItem.Available));
    }
}
