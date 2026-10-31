using Xunit;

namespace Shop.Modules.Inventory.Tests;

public class SmokeTests
{
    [Fact]
    public void ModuleAssembly_IsIsolatedAndCompiles()
    {
        var module = new InventoryModule();
        Assert.Equal("inventory", module.Name);
    }
}
