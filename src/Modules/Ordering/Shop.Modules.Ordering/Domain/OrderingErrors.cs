using Shop.BuildingBlocks.Abstractions;

namespace Shop.Modules.Ordering.Domain;

internal static class OrderingErrors
{
    public static readonly Error EmptyOrderLines =
        Error.Validation("ordering.empty_lines", "An order must contain at least one line item.");

    public static readonly Error TooManyOrderLines =
        Error.Validation("ordering.too_many_lines", "An order may not contain more than 50 line items.");

    public static Error InvalidQuantity(Guid productId, int quantity) =>
        Error.Validation("ordering.invalid_quantity",
            $"Quantity for product '{productId}' must be between 1 and 100. Provided: {quantity}.");

    public static Error DuplicateProduct(Guid productId) =>
        Error.Validation("ordering.duplicate_product",
            $"Duplicate product '{productId}' found in order lines.");

    public static Error InvalidPrice(Guid productId, decimal price) =>
        Error.Validation("ordering.invalid_price",
            $"Unit price for product '{productId}' must be greater than zero. Provided: {price}.");

    public static Error OrderNotFound(Guid orderId) =>
        Error.NotFound("ordering.order_not_found", $"Order '{orderId}' was not found.");

    public static Error UnknownProduct(Guid productId) =>
        Error.UnprocessableEntity("ordering.unknown_product", $"Product '{productId}' was not found in the catalog.");

    public static Error InactiveProduct(Guid productId) =>
        Error.UnprocessableEntity("ordering.inactive_product", $"Product '{productId}' is inactive and cannot be ordered.");

    public static Error CannotCancelNonPendingOrder(OrderStatus current) =>
        Error.Conflict("ordering.cannot_cancel",
            $"Only pending orders can be cancelled. Current status is '{current}'.");

    public static Error InvalidStatusTransition(OrderStatus current, OrderStatus target) =>
        Error.Conflict("ordering.invalid_status_transition",
            $"Cannot transition order from '{current}' to '{target}'.");
}
