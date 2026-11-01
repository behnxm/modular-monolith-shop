using Microsoft.Extensions.Time.Testing;
using Shop.BuildingBlocks.Abstractions;
using Shop.Modules.Notifications.Domain;
using Shouldly;
using Xunit;

namespace Shop.Modules.Notifications.Tests;

public sealed class NotificationsDomainTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Create_ValidParameters_Succeeds()
    {
        var result = Notification.Create(
            "cust_123",
            NotificationKind.OrderConfirmed,
            "Order Confirmed",
            "Your order is confirmed.",
            _timeProvider);

        result.IsSuccess.ShouldBeTrue();
        var notification = result.Value;
        notification.Id.ShouldNotBe(Guid.Empty);
        notification.CustomerId.ShouldBe("cust_123");
        notification.Kind.ShouldBe(NotificationKind.OrderConfirmed);
        notification.Subject.ShouldBe("Order Confirmed");
        notification.Body.ShouldBe("Your order is confirmed.");
        notification.CreatedAt.ShouldBe(_timeProvider.GetUtcNow());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyCustomerId_FailsWithValidation(string customerId)
    {
        var result = Notification.Create(
            customerId,
            NotificationKind.OrderConfirmed,
            "Order Confirmed",
            "Your order is confirmed.",
            _timeProvider);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("notifications.empty_customer_id");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptySubject_FailsWithValidation(string subject)
    {
        var result = Notification.Create(
            "cust_123",
            NotificationKind.OrderConfirmed,
            subject,
            "Your order is confirmed.",
            _timeProvider);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("notifications.empty_subject");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyBody_FailsWithValidation(string body)
    {
        var result = Notification.Create(
            "cust_123",
            NotificationKind.OrderConfirmed,
            "Order Confirmed",
            body,
            _timeProvider);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("notifications.empty_body");
    }
}
