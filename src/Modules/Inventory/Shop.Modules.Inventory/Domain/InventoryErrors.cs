using Shop.BuildingBlocks.Abstractions;

namespace Shop.Modules.Inventory.Domain;

internal static class InventoryErrors
{
    public static readonly Error NegativeOnHandStock =
        Error.Validation("inventory.negative_on_hand", "On-hand stock cannot be negative.");

    public static readonly Error InvalidStockQuantity =
        Error.Validation("inventory.invalid_quantity", "Stock quantity must be greater than zero.");

    public static Error StockDropBelowReserved(int requested, int reserved) =>
        Error.Conflict("inventory.stock_below_reserved",
            $"Cannot reduce on-hand stock to {requested} because {reserved} units are currently reserved.");

    public static Error InsufficientStock(Guid productId, int requested, int available) =>
        Error.Conflict("inventory.insufficient_stock",
            $"Insufficient stock for product '{productId}'. Requested: {requested}, Available: {available}.");

    public static Error StockItemNotFound(Guid productId) =>
        Error.NotFound("inventory.stock_item_not_found", $"Stock item for product '{productId}' was not found.");

    public static Error ReservationNotFound(Guid orderId) =>
        Error.NotFound("inventory.reservation_not_found", $"Reservation for order '{orderId}' was not found.");

    public static Error ReservedQuantityTooLow(int requested, int reserved) =>
        Error.Conflict("inventory.reserved_quantity_too_low",
            $"Cannot commit {requested} units because only {reserved} units are reserved.");

    public static Error InvalidReservationTransition(ReservationStatus current, ReservationStatus target) =>
        Error.Conflict("inventory.invalid_reservation_transition",
            $"Cannot transition reservation from '{current}' to '{target}'.");
}
