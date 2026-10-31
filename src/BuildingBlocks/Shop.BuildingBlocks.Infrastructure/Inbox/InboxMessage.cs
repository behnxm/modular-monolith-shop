namespace Shop.BuildingBlocks.Infrastructure.Inbox;

public sealed class InboxMessage
{
    public Guid MessageId { get; set; }
    public string HandlerName { get; set; } = string.Empty;
    public DateTimeOffset ProcessedOn { get; set; }
}
