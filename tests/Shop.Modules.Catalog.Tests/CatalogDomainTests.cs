using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shop.BuildingBlocks.Testing;
using Shop.Modules.Catalog.Application.CreateProduct;
using Shop.Modules.Catalog.Application.DeactivateProduct;
using Shop.Modules.Catalog.Application.GetProduct;
using Shop.Modules.Catalog.Application.ListProducts;
using Shop.Modules.Catalog.Application.UpdateProduct;
using Shop.Modules.Catalog.Contracts.IntegrationEvents.V1;
using Shop.Modules.Catalog.Domain;
using Shop.Modules.Catalog.Infrastructure;
using Shop.Modules.Catalog.PublicApi;
using Shouldly;
using Xunit;

namespace Shop.Modules.Catalog.Tests;

public class CatalogDomainTests
{
    [Fact]
    public void Sku_ValidValue_CreatesSuccessfully()
    {
        var skuResult = Sku.Create("PROD-001");
        skuResult.IsSuccess.ShouldBeTrue();
        skuResult.Value.Value.ShouldBe("PROD-001");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("AB")]
    [InlineData("THIS-SKU-IS-WAY-TOO-LONG-BECAUSE-IT-EXCEEDS-32-CHARS")]
    [InlineData("prod-001")] // lowercase should be normalized to uppercase or allowed if uppercase
    [InlineData("PROD_001")] // underscore not allowed, only hyphen
    [InlineData("PROD@001")]
    public void Sku_InvalidValues_FailOrNormalize(string input)
    {
        var result = Sku.Create(input);
        if (input == "prod-001")
        {
            // Sku normalizes to uppercase
            result.IsSuccess.ShouldBeTrue();
            result.Value.Value.ShouldBe("PROD-001");
        }
        else
        {
            result.IsFailure.ShouldBeTrue();
        }
    }

    [Fact]
    public void Product_Create_ValidInputs_CreatesWithGuidV7AndInitialVersion()
    {
        var time = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(time);

        var productResult = Product.Create("PHONE-X", "Smart Phone X", "Latest flagship phone", 999.99m, timeProvider);

        productResult.IsSuccess.ShouldBeTrue();
        var product = productResult.Value;
        product.Id.ShouldNotBe(Guid.Empty);
        product.Sku.Value.ShouldBe("PHONE-X");
        product.Name.ShouldBe("Smart Phone X");
        product.Price.ShouldBe(999.99m);
        product.Currency.ShouldBe("USD");
        product.IsActive.ShouldBeTrue();
        product.CreatedAt.ShouldBe(time);
        product.Version.ShouldBe(1);
    }

    [Fact]
    public void Product_Update_IncrementsVersion()
    {
        var timeProvider = new FakeTimeProvider();
        var product = Product.Create("BOOK-01", "C# in Depth", "Great book", 49.99m, timeProvider).Value;

        var updateResult = product.Update("C# in Depth 5th Ed", "Updated edition", 59.99m);

        updateResult.IsSuccess.ShouldBeTrue();
        product.Name.ShouldBe("C# in Depth 5th Ed");
        product.Price.ShouldBe(59.99m);
        product.Version.ShouldBe(2);
    }

    [Fact]
    public void Product_Deactivate_MarksInactiveAndIncrementsVersion()
    {
        var timeProvider = new FakeTimeProvider();
        var product = Product.Create("BOOK-01", "C# in Depth", "Great book", 49.99m, timeProvider).Value;

        product.Deactivate();

        product.IsActive.ShouldBeFalse();
        product.Version.ShouldBe(2);

        // Deactivating already inactive product does not increment version
        product.Deactivate();
        product.Version.ShouldBe(2);
    }
}
