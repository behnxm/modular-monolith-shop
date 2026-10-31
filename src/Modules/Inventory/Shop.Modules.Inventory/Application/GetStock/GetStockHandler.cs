using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Abstractions;
using Shop.Modules.Inventory.Domain;
using Shop.Modules.Inventory.Infrastructure;

namespace Shop.Modules.Inventory.Application.GetStock;

internal sealed class GetStockHandler(InventoryDbContext dbContext)
{
    public async Task<Result<StockResponse>> HandleAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        var stockItem = await dbContext.StockItems
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProductId == productId, cancellationToken);

        if (stockItem is null)
        {
            return Result<StockResponse>.Failure(InventoryErrors.StockItemNotFound(productId));
        }

        return Result<StockResponse>.Success(
            new StockResponse(
                stockItem.ProductId,
                stockItem.OnHand,
                stockItem.Reserved,
                stockItem.Available));
    }
}
