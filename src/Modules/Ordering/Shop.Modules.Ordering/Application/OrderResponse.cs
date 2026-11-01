namespace Shop.Modules.Ordering.Application;

internal sealed record OrderLineResponse(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

internal sealed record OrderResponse(
    Guid Id,
    string CustomerId,
    string Status,
    IReadOnlyList<OrderLineResponse> Lines,
    decimal TotalAmount,
    string Currency,
    string? CancellationReason,
    DateTimeOffset CreatedAt);

internal sealed record OrderPlacementResult(
    OrderResponse Order,
    bool IsReplayed);
