using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Shop.Modules.Catalog.Endpoints;

internal static class CatalogEndpoints
{
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("api/v1/catalog")
            .WithTags("Catalog");
    }
}
