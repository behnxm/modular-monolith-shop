namespace Shop.Modules.Ordering.Domain;

internal sealed class IdempotencyRecord
{
    private IdempotencyRecord()
    {
    }

    public IdempotencyRecord(string customerId, string key, string requestHash, Guid orderId, DateTimeOffset createdAt)
    {
        CustomerId = customerId;
        Key = key;
        RequestHash = requestHash;
        OrderId = orderId;
        CreatedAt = createdAt;
    }

    public string CustomerId { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public Guid OrderId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
