using System.ComponentModel.DataAnnotations;

namespace Shop.Modules.Catalog.Application.UpdateProduct;

internal sealed record UpdateProductRequest(
    [Required] string Name,
    string Description,
    [Range(0.01, 1000000)] decimal Price);
