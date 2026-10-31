namespace Shop.BuildingBlocks.Infrastructure.Outbox;

public sealed class OutboxProcessorOptions
{
    public int PollIntervalMs { get; set; } = 500;
    public int BatchSize { get; set; } = 50;
    public int MaxAttempts { get; set; } = 5;
}
