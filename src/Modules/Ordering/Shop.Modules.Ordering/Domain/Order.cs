using Shop.BuildingBlocks.Abstractions;

namespace Shop.Modules.Ordering.Domain;

internal sealed class Order
{
    private readonly List<OrderLine> _lines = [];

    private Order()
    {
    }

    public Guid Id { get; private set; }
    public string CustomerId { get; private set; } = string.Empty;
    public OrderStatus Status { get; private set; }
    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();
    public decimal TotalAmount { get; private set; }
    public string Currency { get; private set; } = "USD";
    public string? CancellationReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public uint Version { get; internal set; }

    public static Result<Order> Create(
        string customerId,
        IReadOnlyList<(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity)> items)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return Result<Order>.Failure(Error.Validation("ordering.empty_customer", "Customer ID cannot be empty."));
        }

        if (items.Count == 0)
        {
            return Result<Order>.Failure(OrderingErrors.EmptyOrderLines);
        }

        if (items.Count > 50)
        {
            return Result<Order>.Failure(OrderingErrors.TooManyOrderLines);
        }

        var seenProducts = new HashSet<Guid>();
        var orderId = Guid.CreateVersion7();
        var lines = new List<OrderLine>(items.Count);
        decimal total = 0;

        foreach (var item in items)
        {
            if (!seenProducts.Add(item.ProductId))
            {
                return Result<Order>.Failure(OrderingErrors.DuplicateProduct(item.ProductId));
            }

            if (item.Quantity is < 1 or > 100)
            {
                return Result<Order>.Failure(OrderingErrors.InvalidQuantity(item.ProductId, item.Quantity));
            }

            if (item.UnitPrice <= 0)
            {
                return Result<Order>.Failure(OrderingErrors.InvalidPrice(item.ProductId, item.UnitPrice));
            }

            var line = new OrderLine(orderId, item.ProductId, item.ProductName, item.UnitPrice, item.Quantity);
            lines.Add(line);
            total += line.LineTotal;
        }

        var order = new Order
        {
            Id = orderId,
            CustomerId = customerId,
            Status = OrderStatus.Pending,
            TotalAmount = total,
            Currency = "USD",
            CreatedAt = DateTimeOffset.UtcNow
        };

        order._lines.AddRange(lines);

        return Result<Order>.Success(order);
    }

    public Result Confirm()
    {
        if (Status == OrderStatus.Confirmed)
        {
            return Result.Success();
        }

        if (Status != OrderStatus.Pending)
        {
            return Result.Failure(OrderingErrors.InvalidStatusTransition(Status, OrderStatus.Confirmed));
        }

        Status = OrderStatus.Confirmed;
        return Result.Success();
    }

    public Result Cancel(string reason)
    {
        if (Status == OrderStatus.Cancelled)
        {
            return Result.Success();
        }

        if (Status != OrderStatus.Pending)
        {
            return Result.Failure(OrderingErrors.CannotCancelNonPendingOrder(Status));
        }

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        return Result.Success();
    }
}
