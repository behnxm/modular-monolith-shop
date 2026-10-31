using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Shop.BuildingBlocks.Infrastructure.Telemetry;

public static class ShopTelemetry
{
    public const string ServiceName = "Shop.Modules";

    public static readonly ActivitySource ActivitySource = new(ServiceName);
    public static readonly Meter Meter = new(ServiceName);

    public static readonly Counter<long> OutboxFailedTotal =
        Meter.CreateCounter<long>(
            "shop_outbox_failed_total",
            description: "Total number of outbox messages that exceeded max delivery attempts.");

    public static readonly Counter<long> EventsPublishedTotal =
        Meter.CreateCounter<long>(
            "shop_events_published_total",
            description: "Total number of integration events published via outbox.");

    public static readonly Counter<long> EventsHandledTotal =
        Meter.CreateCounter<long>(
            "shop_events_handled_total",
            description: "Total number of integration event handling attempts by handler and result.");

    public static readonly Histogram<double> EventHandleDurationSeconds =
        Meter.CreateHistogram<double>(
            "shop_event_handle_duration_seconds",
            unit: "s",
            description: "Duration of integration event handler execution in seconds.");
}
