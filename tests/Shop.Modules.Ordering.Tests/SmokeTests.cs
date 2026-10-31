using Xunit;

namespace Shop.Modules.Ordering.Tests;

public class SmokeTests
{
    [Fact]
    public void ModuleAssembly_IsIsolatedAndCompiles()
    {
        var module = new OrderingModule();
        Assert.Equal("ordering", module.Name);
    }
}
