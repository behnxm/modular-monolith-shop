using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shop.BuildingBlocks.Testing;
using Shop.Modules.Inventory.Contracts.IntegrationEvents.V1;
using Shop.Modules.Ordering.Contracts.IntegrationEvents.V1;
using Shop.Modules.Payments.Application.EventHandlers;
using Shop.Modules.Payments.Application.GetPaymentAttempt;
using Shop.Modules.Payments.Domain;
using Shop.Modules.Payments.Infrastructure;
using Shouldly;
using Xunit;

namespace Shop.Modules.Payments.Tests;

public sealed class PaymentsIntegrationTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private PaymentsDbContext _dbContext = null!;
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 1, 16, 0, 0, TimeSpan.Zero));
    private readonly IPaymentGateway _paymentGateway = new FakePaymentGateway(new ConfigurationBuilder().Build());

    public PaymentsIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        var migratorOptions = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseNpgsql(_fixture.GetMigratorConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "payments"))
            .Options;

        await using var migratorContext = new PaymentsDbContext(migratorOptions);
        await PostgreSqlFixture.MigrateContextAsync(migratorContext);

        _dbContext = CreateAppDbContext();
        await _fixture.ResetSchemasAsync("payments");
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    private PaymentsDbContext CreateAppDbContext()
    {
        var appOptions = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseNpgsql(_fixture.GetAppConnectionString("payments"), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "payments"))
            .Options;
        return new PaymentsDbContext(appOptions);
    }

    [Fact]
    public async Task OrderPlaced_RecordsPendingPaymentAttempt_Idempotently()
    {
        var ct = TestContext.Current.CancellationToken;
        var orderId = Guid.CreateVersion7();
        var handler = new OrderPlacedIntegrationEventHandler(_dbContext);
        var evt = new OrderPlacedIntegrationEvent(orderId, "cust_01", [], 250.00m, "USD");

        // First run
        await handler.HandleAsync(evt, ct);
        await _dbContext.SaveChangesAsync(ct);

        await using var verifyDb1 = CreateAppDbContext();
        var attempt = await verifyDb1.PaymentAttempts.FirstAsync(p => p.OrderId == orderId, ct);
        attempt.Amount.ShouldBe(250.00m);
        attempt.Status.ShouldBe(PaymentStatus.Pending);

        // Second run (idempotent replay)
        await handler.HandleAsync(evt, ct);
        await _dbContext.SaveChangesAsync(ct);

        await using var verifyDb2 = CreateAppDbContext();
        var count = await verifyDb2.PaymentAttempts.CountAsync(p => p.OrderId == orderId, ct);
        count.ShouldBe(1);
    }

    [Fact]
    public async Task StockReserved_ChargesGateway_AndEnqueuesPaymentSucceeded()
    {
        var ct = TestContext.Current.CancellationToken;
        var orderId = Guid.CreateVersion7();
        _dbContext.PaymentAttempts.Add(PaymentAttempt.CreatePending(orderId, 500.00m, "USD"));
        await _dbContext.SaveChangesAsync(ct);

        var handler = new StockReservedIntegrationEventHandler(
            _dbContext,
            _paymentGateway,
            _timeProvider,
            NullLogger<StockReservedIntegrationEventHandler>.Instance);

        await handler.HandleAsync(new StockReservedIntegrationEvent(orderId), ct);
        await _dbContext.SaveChangesAsync(ct);

        await using var verifyDb = CreateAppDbContext();
        var attempt = await verifyDb.PaymentAttempts.FirstAsync(p => p.OrderId == orderId, ct);
        attempt.Status.ShouldBe(PaymentStatus.Succeeded);

        var outboxMsg = await verifyDb.OutboxMessages.FirstAsync(m => m.ContractName == "payments.payment-succeeded.v1", ct);
        outboxMsg.Payload.ShouldContain(orderId.ToString());
    }

    [Fact]
    public async Task StockReserved_OverLimit_FailsAndEnqueuesPaymentFailed()
    {
        var ct = TestContext.Current.CancellationToken;
        var orderId = Guid.CreateVersion7();
        _dbContext.PaymentAttempts.Add(PaymentAttempt.CreatePending(orderId, 15000.00m, "USD")); // Over $10,000 limit
        await _dbContext.SaveChangesAsync(ct);

        var handler = new StockReservedIntegrationEventHandler(
            _dbContext,
            _paymentGateway,
            _timeProvider,
            NullLogger<StockReservedIntegrationEventHandler>.Instance);

        await handler.HandleAsync(new StockReservedIntegrationEvent(orderId), ct);
        await _dbContext.SaveChangesAsync(ct);

        await using var verifyDb = CreateAppDbContext();
        var attempt = await verifyDb.PaymentAttempts.FirstAsync(p => p.OrderId == orderId, ct);
        attempt.Status.ShouldBe(PaymentStatus.Failed);
        attempt.FailureReason.ShouldNotBeNull();
        attempt.FailureReason.ShouldContain("exceeds maximum approval limit");

        var outboxMsg = await verifyDb.OutboxMessages.FirstAsync(m => m.ContractName == "payments.payment-failed.v1", ct);
        outboxMsg.Payload.ShouldContain(orderId.ToString());
    }

    [Fact]
    public async Task OrderCancelled_RefundsSucceededPayment_AndEnqueuesPaymentRefunded()
    {
        var ct = TestContext.Current.CancellationToken;
        var orderId = Guid.CreateVersion7();
        var attempt = PaymentAttempt.CreatePending(orderId, 100.00m, "USD");
        attempt.MarkSucceeded();
        _dbContext.PaymentAttempts.Add(attempt);
        await _dbContext.SaveChangesAsync(ct);

        var handler = new OrderCancelledIntegrationEventHandler(_dbContext, _timeProvider);
        await handler.HandleAsync(new OrderCancelledIntegrationEvent(orderId, "cust_01", "Changed mind"), ct);
        await _dbContext.SaveChangesAsync(ct);

        await using var verifyDb = CreateAppDbContext();
        var updated = await verifyDb.PaymentAttempts.FirstAsync(p => p.OrderId == orderId, ct);
        updated.Status.ShouldBe(PaymentStatus.Refunded);

        var outboxMsg = await verifyDb.OutboxMessages.FirstAsync(m => m.ContractName == "payments.payment-refunded.v1", ct);
        outboxMsg.Payload.ShouldContain(orderId.ToString());
    }

    [Fact]
    public async Task OrderCancelled_Before_OrderPlaced_Tolerated()
    {
        var ct = TestContext.Current.CancellationToken;
        var orderId = Guid.CreateVersion7();

        // 1. OrderCancelled arrives FIRST
        var cancelHandler = new OrderCancelledIntegrationEventHandler(_dbContext, _timeProvider);
        await cancelHandler.HandleAsync(new OrderCancelledIntegrationEvent(orderId, "cust_01", "Early cancel"), ct);
        await _dbContext.SaveChangesAsync(ct);

        await using var verifyDb1 = CreateAppDbContext();
        var placeholder = await verifyDb1.PaymentAttempts.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        placeholder.ShouldNotBeNull();
        placeholder.Status.ShouldBe(PaymentStatus.Failed);

        // 2. Later StockReserved arrives
        var stockHandler = new StockReservedIntegrationEventHandler(
            _dbContext,
            _paymentGateway,
            _timeProvider,
            NullLogger<StockReservedIntegrationEventHandler>.Instance);

        await stockHandler.HandleAsync(new StockReservedIntegrationEvent(orderId), ct);
        await _dbContext.SaveChangesAsync(ct);

        // Verify status remains Failed (not charged)
        await using var verifyDb2 = CreateAppDbContext();
        var finalAttempt = await verifyDb2.PaymentAttempts.FirstAsync(p => p.OrderId == orderId, ct);
        finalAttempt.Status.ShouldBe(PaymentStatus.Failed);

        // No payment succeeded event enqueued
        var succeededMsg = await verifyDb2.OutboxMessages.FirstOrDefaultAsync(m => m.ContractName == "payments.payment-succeeded.v1", ct);
        succeededMsg.ShouldBeNull();
    }

    [Fact]
    public async Task GetPaymentAttempt_ReturnsCorrectData_OrNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var orderId = Guid.CreateVersion7();
        _dbContext.PaymentAttempts.Add(PaymentAttempt.CreatePending(orderId, 300m, "USD"));
        await _dbContext.SaveChangesAsync(ct);

        var handler = new GetPaymentAttemptHandler(_dbContext);

        // Success
        var result = await handler.HandleAsync(orderId, ct);
        result.IsSuccess.ShouldBeTrue();
        result.Value.OrderId.ShouldBe(orderId);
        result.Value.Amount.ShouldBe(300m);
        result.Value.Status.ShouldBe("Pending");

        // Missing
        var missingResult = await handler.HandleAsync(Guid.CreateVersion7(), ct);
        missingResult.IsFailure.ShouldBeTrue();
        missingResult.Error.Code.ShouldBe("payments.attempt_not_found");
    }
}
