using Shop.Modules.Inventory.Domain;
using Shouldly;
using Xunit;

namespace Shop.Modules.Inventory.Tests;

public sealed class InventoryDomainTests
{
    [Fact]
    public void Create_WithValidParameters_InitializesCorrectly()
    {
        var productId = Guid.CreateVersion7();
        var stockItem = StockItem.Create(productId, 100);

        stockItem.ProductId.ShouldBe(productId);
        stockItem.OnHand.ShouldBe(100);
        stockItem.Reserved.ShouldBe(0);
        stockItem.Available.ShouldBe(100);
    }

    [Fact]
    public void Create_WithNegativeOnHand_ThrowsArgumentOutOfRangeException()
    {
        var productId = Guid.CreateVersion7();
        Should.Throw<ArgumentOutOfRangeException>(() => StockItem.Create(productId, -5));
    }

    [Fact]
    public void SetOnHand_WhenNegative_ReturnsValidationError()
    {
        var stockItem = StockItem.Create(Guid.CreateVersion7(), 10);
        var result = stockItem.SetOnHand(-1);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("inventory.negative_on_hand");
    }

    [Fact]
    public void SetOnHand_WhenBelowReserved_ReturnsConflictError()
    {
        var stockItem = StockItem.Create(Guid.CreateVersion7(), 100);
        // Reserve 20
        var reservation = Reservation.Create(Guid.CreateVersion7(), [(stockItem.ProductId, 20)]);
        stockItem.GetType().GetProperty(nameof(StockItem.Reserved))!
            .SetValue(stockItem, 20);

        var result = stockItem.SetOnHand(15);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("inventory.stock_below_reserved");
    }

    [Fact]
    public void CommitReservation_DecreasesBothOnHandAndReserved()
    {
        var stockItem = StockItem.Create(Guid.CreateVersion7(), 50);
        stockItem.GetType().GetProperty(nameof(StockItem.Reserved))!
            .SetValue(stockItem, 20);

        var result = stockItem.CommitReservation(15);

        result.IsSuccess.ShouldBeTrue();
        stockItem.OnHand.ShouldBe(35);
        stockItem.Reserved.ShouldBe(5);
        stockItem.Available.ShouldBe(30);
    }

    [Fact]
    public void ReleaseReservation_DecreasesReservedOnly()
    {
        var stockItem = StockItem.Create(Guid.CreateVersion7(), 50);
        stockItem.GetType().GetProperty(nameof(StockItem.Reserved))!
            .SetValue(stockItem, 20);

        var result = stockItem.ReleaseReservation(15);

        result.IsSuccess.ShouldBeTrue();
        stockItem.OnHand.ShouldBe(50);
        stockItem.Reserved.ShouldBe(5);
        stockItem.Available.ShouldBe(45);
    }

    [Fact]
    public void Reservation_Transitions_Correctly()
    {
        var orderId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var reservation = Reservation.Create(orderId, [(productId, 5)]);

        reservation.Status.ShouldBe(ReservationStatus.Reserved);
        reservation.Lines.Count.ShouldBe(1);
        reservation.Lines[0].ProductId.ShouldBe(productId);
        reservation.Lines[0].Quantity.ShouldBe(5);

        var commitResult = reservation.Commit();
        commitResult.IsSuccess.ShouldBeTrue();
        reservation.Status.ShouldBe(ReservationStatus.Committed);

        // Re-committing is idempotent
        reservation.Commit().IsSuccess.ShouldBeTrue();

        // Transition from Committed to Released should fail
        var releaseResult = reservation.Release();
        releaseResult.IsFailure.ShouldBeTrue();
    }
}
