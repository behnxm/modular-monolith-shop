namespace Shop.Modules.Catalog.Contracts;

public interface ICatalogApi
{
    Task<IReadOnlyList<ProductSnapshot>> GetProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken);
}
