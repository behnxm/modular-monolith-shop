using Microsoft.AspNetCore.Http;
using Shop.BuildingBlocks.Abstractions;

namespace Shop.BuildingBlocks.Infrastructure.Web;

public static class IdempotencyKeyParser
{
    public const string HeaderName = "Idempotency-Key";
    public const int MaxLength = 64;

    public static Result<string> Parse(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (!httpContext.Request.Headers.TryGetValue(HeaderName, out var values) ||
            string.IsNullOrWhiteSpace(values))
        {
            return Result<string>.Failure(
                Error.Validation("idempotency.key_missing", "The 'Idempotency-Key' request header is required."));
        }

        var key = values.ToString().Trim();
        if (key.Length > MaxLength)
        {
            return Result<string>.Failure(
                Error.Validation("idempotency.key_too_long", $"The 'Idempotency-Key' header must not exceed {MaxLength} characters."));
        }

        return Result<string>.Success(key);
    }
}
