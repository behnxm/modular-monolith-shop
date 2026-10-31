using Shop.BuildingBlocks.Abstractions;

namespace Shop.Modules.Catalog.Domain;

internal sealed class Product
{
    public const string DefaultCurrency = "USD";
    public const decimal MaxPrice = 1_000_000m;

    private Product() { } // EF Core constructor

    public Guid Id { get; private set; }
    public Sku Sku { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = DefaultCurrency;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int Version { get; private set; }

    public static Result<Product> Create(
        string rawSku,
        string rawName,
        string rawDescription,
        decimal price,
        TimeProvider timeProvider)
    {
        var skuResult = Sku.Create(rawSku);
        if (skuResult.IsFailure)
        {
            return Result<Product>.Failure(skuResult.Error);
        }

        var validationError = ValidateAttributes(rawName, rawDescription, price);
        if (validationError is not null)
        {
            return Result<Product>.Failure(validationError);
        }

        var now = timeProvider.GetUtcNow();
        var product = new Product
        {
            Id = Guid.CreateVersion7(now),
            Sku = skuResult.Value,
            Name = rawName.Trim(),
            Description = rawDescription?.Trim() ?? string.Empty,
            Price = decimal.Round(price, 2, MidpointRounding.AwayFromZero),
            Currency = DefaultCurrency,
            IsActive = true,
            CreatedAt = now,
            Version = 1
        };

        return Result<Product>.Success(product);
    }

    public Result Update(string rawName, string rawDescription, decimal newPrice)
    {
        var validationError = ValidateAttributes(rawName, rawDescription, newPrice);
        if (validationError is not null)
        {
            return Result.Failure(validationError);
        }

        Name = rawName.Trim();
        Description = rawDescription?.Trim() ?? string.Empty;
        Price = decimal.Round(newPrice, 2, MidpointRounding.AwayFromZero);
        Version++;

        return Result.Success();
    }

    public void Deactivate()
    {
        if (IsActive)
        {
            IsActive = false;
            Version++;
        }
    }

    private static Error? ValidateAttributes(string? name, string? description, decimal price)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length is < 1 or > 200)
        {
            return CatalogErrors.InvalidName("Product name must be between 1 and 200 characters.");
        }

        if (description is not null && description.Length > 2000)
        {
            return CatalogErrors.InvalidDescription("Product description must not exceed 2000 characters.");
        }

        if (price <= 0 || price > MaxPrice)
        {
            return CatalogErrors.InvalidPrice($"Price must be greater than zero and at most {MaxPrice:N0}.");
        }

        if (decimal.Round(price, 2) != price)
        {
            return CatalogErrors.InvalidPrice("Price cannot have more than 2 decimal places.");
        }

        return null;
    }
}
