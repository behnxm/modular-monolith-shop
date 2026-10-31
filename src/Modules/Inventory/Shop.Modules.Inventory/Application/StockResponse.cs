namespace Shop.Modules.Inventory.Application;

internal sealed record StockResponse(
    Guid ProductId,
    int OnHand,
    int Reserved,
    int Available);
