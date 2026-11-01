using Shop.Modules.Ordering.Domain;
using Shouldly;
using Xunit;

namespace Shop.Modules.Ordering.Tests;

public sealed class OrderingDomainTests
{
    [Fact]
    public void Create_WithValidParameters_ComputesTotalAndSetsPending()
    {
        var p1 = Guid.CreateVersion7();
        var p2 = Guid.CreateVersion7();

        var result = Order.Create("cust_01", [
            (p1, "Product 1", 20.00m, 2),
            (p2, "Product 2", 15.50m, 3)
        ]);

        result.IsSuccess.ShouldBeTrue();
        var order = result.Value;

        order.CustomerId.ShouldBe("cust_01");
        order.Status.ShouldBe(OrderStatus.Pending);
        order.Lines.Count.ShouldBe(2);
        order.TotalAmount.ShouldBe(86.50m);
        order.Currency.ShouldBe("USD");
    }

    [Fact]
    public void Create_WithEmptyLines_Fails()
    {
        var result = Order.Create("cust_01", []);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ordering.empty_lines");
    }

    [Fact]
    public void Create_WithMoreThan50Lines_Fails()
    {
        var items = Enumerable.Range(1, 51)
            .Select(i => (Guid.CreateVersion7(), $"Item {i}", 10.00m, 1))
            .ToList();

        var result = Order.Create("cust_01", items);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ordering.too_many_lines");
    }

    [Fact]
    public void Create_WithDuplicateProduct_Fails()
    {
        var p1 = Guid.CreateVersion7();
        var result = Order.Create("cust_01", [
            (p1, "Product 1", 10.00m, 1),
            (p1, "Product 1 Duplicate", 10.00m, 2)
        ]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ordering.duplicate_product");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Create_WithInvalidQuantity_Fails(int quantity)
    {
        var p1 = Guid.CreateVersion7();
        var result = Order.Create("cust_01", [
            (p1, "Product 1", 10.00m, quantity)
        ]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ordering.invalid_quantity");
    }

    [Fact]
    public void StatusMachine_PendingToConfirmed_AndTerminal()
    {
        var p1 = Guid.CreateVersion7();
        var order = Order.Create("cust_01", [(p1, "Product 1", 10.00m, 1)]).Value;

        var confirmResult = order.Confirm();
        confirmResult.IsSuccess.ShouldBeTrue();
        order.Status.ShouldBe(OrderStatus.Confirmed);

        // Re-confirm is idempotent
        order.Confirm().IsSuccess.ShouldBeTrue();

        // Cancelling confirmed order must fail
        var cancelResult = order.Cancel("Changed mind");
        cancelResult.IsFailure.ShouldBeTrue();
        cancelResult.Error.Code.ShouldBe("ordering.cannot_cancel");
    }

    [Fact]
    public void StatusMachine_PendingToCancelled_AndTerminal()
    {
        var p1 = Guid.CreateVersion7();
        var order = Order.Create("cust_01", [(p1, "Product 1", 10.00m, 1)]).Value;

        var cancelResult = order.Cancel("User requested cancellation");
        cancelResult.IsSuccess.ShouldBeTrue();
        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.CancellationReason.ShouldBe("User requested cancellation");

        // Re-cancelling is idempotent
        order.Cancel("Repeated").IsSuccess.ShouldBeTrue();

        // Confirming cancelled order must fail
        var confirmResult = order.Confirm();
        confirmResult.IsFailure.ShouldBeTrue();
        confirmResult.Error.Code.ShouldBe("ordering.invalid_status_transition");
    }
}
