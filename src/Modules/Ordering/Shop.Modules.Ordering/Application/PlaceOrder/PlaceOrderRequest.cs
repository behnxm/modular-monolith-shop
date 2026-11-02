namespace Shop.Modules.Ordering.Application.PlaceOrder;

internal sealed record PlaceOrderItemRequest(Guid ProductId, int Quantity);

internal sealed record PlaceOrderRequest(IReadOnlyList<PlaceOrderItemRequest> Lines);
