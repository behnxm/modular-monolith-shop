using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Abstractions;
using Shop.BuildingBlocks.Infrastructure.Outbox;
using Shop.Modules.Catalog.Contracts;
using Shop.Modules.Ordering.Contracts.IntegrationEvents.V1;
using Shop.Modules.Ordering.Domain;
using Shop.Modules.Ordering.Infrastructure;

namespace Shop.Modules.Ordering.Application.PlaceOrder;

internal sealed class PlaceOrderHandler(
    OrderingDbContext dbContext,
    ICatalogApi catalogApi,
    TimeProvider timeProvider)
{
    public async Task<Result<OrderPlacementResult>> HandleAsync(
        string customerId,
        string idempotencyKey,
        PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return Result<OrderPlacementResult>.Failure(Error.Validation("ordering.empty_customer", "Customer ID cannot be empty."));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 64)
        {
            return Result<OrderPlacementResult>.Failure(
                Error.Validation("ordering.invalid_idempotency_key", "Idempotency key is required and must not exceed 64 characters."));
        }

        var requestHash = ComputeRequestHash(request);

        // 1. Check existing idempotency record
        var existingRecord = await dbContext.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.CustomerId == customerId && r.Key == idempotencyKey, cancellationToken);

        if (existingRecord is not null)
        {
            if (!string.Equals(existingRecord.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return Result<OrderPlacementResult>.Failure(
                    Error.Conflict("ordering.idempotency_conflict", "Idempotency key was already used with a different request payload."));
            }

            var existingOrder = await dbContext.Orders
                .AsNoTracking()
                .Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == existingRecord.OrderId, cancellationToken);

            if (existingOrder is not null)
            {
                return Result<OrderPlacementResult>.Success(new OrderPlacementResult(MapToResponse(existingOrder), IsReplayed: true));
            }
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Result<OrderPlacementResult>.Failure(OrderingErrors.EmptyOrderLines);
        }

        if (request.Items.Count > 50)
        {
            return Result<OrderPlacementResult>.Failure(OrderingErrors.TooManyOrderLines);
        }

        // 2. Fetch product snapshots synchronously through ICatalogApi
        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var catalogProducts = await catalogApi.GetProductsAsync(productIds, cancellationToken);
        var productDict = catalogProducts.ToDictionary(p => p.Id);

        var orderItems = new List<(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity)>();
        foreach (var item in request.Items)
        {
            if (!productDict.TryGetValue(item.ProductId, out var catalogProduct))
            {
                return Result<OrderPlacementResult>.Failure(OrderingErrors.UnknownProduct(item.ProductId));
            }

            if (!catalogProduct.IsActive)
            {
                return Result<OrderPlacementResult>.Failure(OrderingErrors.InactiveProduct(item.ProductId));
            }

            orderItems.Add((item.ProductId, catalogProduct.Name, catalogProduct.UnitPrice, item.Quantity));
        }

        // 3. Create aggregate
        var createResult = Order.Create(customerId, orderItems);
        if (createResult.IsFailure)
        {
            return Result<OrderPlacementResult>.Failure(createResult.Error);
        }

        var order = createResult.Value;
        var idempotencyRecord = new IdempotencyRecord(customerId, idempotencyKey, requestHash, order.Id, timeProvider.GetUtcNow());

        dbContext.Orders.Add(order);
        dbContext.IdempotencyRecords.Add(idempotencyRecord);

        // 4. Enqueue OrderPlacedIntegrationEvent in outbox
        var linesSnapshot = order.Lines
            .Select(l => new OrderLineSnapshot(l.ProductId, l.Quantity))
            .ToList();

        var orderPlacedEvent = new OrderPlacedIntegrationEvent(
            order.Id,
            order.CustomerId,
            linesSnapshot,
            order.TotalAmount,
            order.Currency);

        dbContext.Enqueue(orderPlacedEvent, timeProvider);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Concurrent request with same idempotency key raced
            var replayedRecord = await dbContext.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.CustomerId == customerId && r.Key == idempotencyKey, cancellationToken);

            if (replayedRecord is not null)
            {
                if (!string.Equals(replayedRecord.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    return Result<OrderPlacementResult>.Failure(
                        Error.Conflict("ordering.idempotency_conflict", "Idempotency key was already used with a different request payload."));
                }

                var existingOrder = await dbContext.Orders
                    .AsNoTracking()
                    .Include(o => o.Lines)
                    .FirstAsync(o => o.Id == replayedRecord.OrderId, cancellationToken);

                return Result<OrderPlacementResult>.Success(new OrderPlacementResult(MapToResponse(existingOrder), IsReplayed: true));
            }

            throw;
        }

        return Result<OrderPlacementResult>.Success(new OrderPlacementResult(MapToResponse(order), IsReplayed: false));
    }

    private static string ComputeRequestHash(PlaceOrderRequest request)
    {
        var items = request.Items?
            .OrderBy(i => i.ProductId)
            .Select(i => $"{i.ProductId}:{i.Quantity}")
            ?? [];
        var normalized = string.Join(";", items);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static OrderResponse MapToResponse(Order order)
    {
        var lineResponses = order.Lines
            .Select(l => new OrderLineResponse(l.ProductId, l.ProductNameSnapshot, l.UnitPriceSnapshot, l.Quantity, l.LineTotal))
            .ToList();

        return new OrderResponse(
            order.Id,
            order.CustomerId,
            order.Status.ToString(),
            lineResponses,
            order.TotalAmount,
            order.Currency,
            order.CancellationReason,
            order.CreatedAt);
    }
}
