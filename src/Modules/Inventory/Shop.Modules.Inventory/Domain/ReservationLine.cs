namespace Shop.Modules.Inventory.Domain;

internal sealed class ReservationLine
{
    private ReservationLine()
    {
    }

    public ReservationLine(Guid orderId, Guid productId, int quantity)
    {
        OrderId = orderId;
        ProductId = productId;
        Quantity = quantity;
    }

    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
}
