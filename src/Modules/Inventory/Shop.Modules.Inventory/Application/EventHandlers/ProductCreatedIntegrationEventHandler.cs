using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Abstractions;
using Shop.Modules.Catalog.Contracts.IntegrationEvents.V1;
using Shop.Modules.Inventory.Domain;
using Shop.Modules.Inventory.Infrastructure;

namespace Shop.Modules.Inventory.Application.EventHandlers;

internal sealed class ProductCreatedIntegrationEventHandler(InventoryDbContext dbContext)
    : IIntegrationEventHandler<ProductCreatedIntegrationEvent>
{
    public async Task HandleAsync(ProductCreatedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var exists = await dbContext.StockItems
            .AnyAsync(s => s.ProductId == integrationEvent.ProductId, cancellationToken);

        if (!exists)
        {
            dbContext.StockItems.Add(StockItem.Create(integrationEvent.ProductId, 0));
        }
    }
}
