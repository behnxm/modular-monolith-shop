using Shop.BuildingBlocks.Abstractions;

namespace Shop.Modules.Inventory.Domain;

internal sealed class Reservation
{
    private readonly List<ReservationLine> _lines = [];

    private Reservation()
    {
    }

    public Guid OrderId { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyList<ReservationLine> Lines => _lines.AsReadOnly();

    public static Reservation Create(Guid orderId, IEnumerable<(Guid ProductId, int Quantity)> lines)
    {
        var reservation = new Reservation
        {
            OrderId = orderId,
            Status = ReservationStatus.Reserved,
            CreatedAt = DateTimeOffset.UtcNow
        };

        foreach (var (productId, quantity) in lines)
        {
            reservation._lines.Add(new ReservationLine(orderId, productId, quantity));
        }

        return reservation;
    }

    public static Reservation CreateReleasedPlaceholder(Guid orderId)
    {
        return new Reservation
        {
            OrderId = orderId,
            Status = ReservationStatus.Released,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public Result Commit()
    {
        if (Status == ReservationStatus.Committed)
        {
            return Result.Success();
        }

        if (Status != ReservationStatus.Reserved)
        {
            return Result.Failure(InventoryErrors.InvalidReservationTransition(Status, ReservationStatus.Committed));
        }

        Status = ReservationStatus.Committed;
        return Result.Success();
    }

    public Result Release()
    {
        if (Status == ReservationStatus.Released)
        {
            return Result.Success();
        }

        if (Status != ReservationStatus.Reserved)
        {
            return Result.Failure(InventoryErrors.InvalidReservationTransition(Status, ReservationStatus.Released));
        }

        Status = ReservationStatus.Released;
        return Result.Success();
    }
}
