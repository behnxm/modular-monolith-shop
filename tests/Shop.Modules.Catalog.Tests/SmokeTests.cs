using Xunit;

namespace Shop.Modules.Catalog.Tests;

public class SmokeTests
{
    [Fact]
    public void ModuleAssembly_IsIsolatedAndCompiles()
    {
        var module = new CatalogModule();
        Assert.Equal("catalog", module.Name);
    }
}
