using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Shop.Modules.Ordering.Application;

internal sealed record ListOrdersQuery(int Limit = 25, string? Cursor = null);

internal sealed record PagedOrdersResponse(
    IReadOnlyList<OrderResponse> Items,
    string? NextCursor,
    bool HasNextPage);

internal static class OrderCursorHelper
{
    public static string EncodeCursor(DateTimeOffset createdAt, Guid id)
    {
        var raw = $"{createdAt.ToUnixTimeMilliseconds()}:{id}";
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(raw));
    }

    public static bool TryDecodeCursor(string cursor, out DateTimeOffset createdAt, out Guid id)
    {
        createdAt = default;
        id = default;

        try
        {
            var bytes = WebEncoders.Base64UrlDecode(cursor);
            var str = Encoding.UTF8.GetString(bytes);
            var parts = str.Split(':');
            if (parts.Length != 2)
            {
                return false;
            }

            if (!long.TryParse(parts[0], out var millis))
            {
                return false;
            }

            if (!Guid.TryParse(parts[1], out id))
            {
                return false;
            }

            createdAt = DateTimeOffset.FromUnixTimeMilliseconds(millis);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
