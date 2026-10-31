using Shop.BuildingBlocks.Abstractions;

namespace Shop.Modules.Inventory.Domain;

internal sealed class StockItem
{
    private StockItem()
    {
    }

    public Guid ProductId { get; private set; }
    public int OnHand { get; private set; }
    public int Reserved { get; private set; }
    public uint Version { get; internal set; }

    public int Available => OnHand - Reserved;

    public static StockItem Create(Guid productId, int onHand = 0)
    {
        if (onHand < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(onHand), "On hand stock cannot be negative.");
        }

        return new StockItem
        {
            ProductId = productId,
            OnHand = onHand,
            Reserved = 0
        };
    }

    public Result SetOnHand(int newOnHand)
    {
        if (newOnHand < 0)
        {
            return Result.Failure(InventoryErrors.NegativeOnHandStock);
        }

        if (newOnHand < Reserved)
        {
            return Result.Failure(InventoryErrors.StockDropBelowReserved(newOnHand, Reserved));
        }

        OnHand = newOnHand;
        return Result.Success();
    }

    public Result CommitReservation(int quantity)
    {
        if (quantity <= 0)
        {
            return Result.Failure(InventoryErrors.InvalidStockQuantity);
        }

        if (Reserved < quantity)
        {
            return Result.Failure(InventoryErrors.ReservedQuantityTooLow(quantity, Reserved));
        }

        OnHand -= quantity;
        Reserved -= quantity;
        return Result.Success();
    }

    public Result ReleaseReservation(int quantity)
    {
        if (quantity <= 0)
        {
            return Result.Failure(InventoryErrors.InvalidStockQuantity);
        }

        Reserved = Math.Max(0, Reserved - quantity);
        return Result.Success();
    }
}
