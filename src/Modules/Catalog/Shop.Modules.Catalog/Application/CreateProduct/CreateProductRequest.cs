using System.ComponentModel.DataAnnotations;

namespace Shop.Modules.Catalog.Application.CreateProduct;

internal sealed record CreateProductRequest(
    [Required] string Sku,
    [Required] string Name,
    string Description,
    [Range(0.01, 1000000)] decimal Price);
