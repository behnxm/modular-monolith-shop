namespace Shop.Modules.Ordering.Domain;

internal sealed class OrderLine
{
    private OrderLine()
    {
    }

    public OrderLine(Guid orderId, Guid productId, string productNameSnapshot, decimal unitPriceSnapshot, int quantity)
    {
        OrderId = orderId;
        ProductId = productId;
        ProductNameSnapshot = productNameSnapshot;
        UnitPriceSnapshot = unitPriceSnapshot;
        Quantity = quantity;
    }

    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductNameSnapshot { get; private set; } = string.Empty;
    public decimal UnitPriceSnapshot { get; private set; }
    public int Quantity { get; private set; }
    public decimal LineTotal => UnitPriceSnapshot * Quantity;
}
