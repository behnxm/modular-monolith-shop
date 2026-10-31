namespace Shop.BuildingBlocks.Infrastructure.Outbox;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string ContractName { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset OccurredOn { get; set; }
    public string? TraceParent { get; set; }
    public string? TraceState { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptOn { get; set; }
    public DateTimeOffset? ProcessedOn { get; set; }
    public DateTimeOffset? FailedOn { get; set; }
    public string? LastError { get; set; }
}
