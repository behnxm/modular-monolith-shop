using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Shop.Modules.Payments.Infrastructure;

internal sealed class FakePaymentGateway(IConfiguration configuration) : IPaymentGateway
{
    private const decimal DefaultApprovalLimit = 10000.00m;

    public Task<PaymentGatewayResult> ChargeAsync(Guid orderId, decimal amount, string currency, CancellationToken cancellationToken)
    {
        var limitConfig = configuration["Modules:payments:ApprovalLimit"];
        var limit = decimal.TryParse(limitConfig, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedLimit)
            ? parsedLimit
            : DefaultApprovalLimit;

        if (amount <= limit)
        {
            var txId = $"tx_{Guid.CreateVersion7():N}"[..16];
            return Task.FromResult(new PaymentGatewayResult(true, txId, null));
        }

        var reason = $"Amount {amount:F2} {currency} exceeds maximum approval limit of {limit:F2} {currency}.";
        return Task.FromResult(new PaymentGatewayResult(false, null, reason));
    }
}
