using Xunit;

namespace Shop.Modules.Payments.Tests;

public class SmokeTests
{
    [Fact]
    public void ModuleAssembly_IsIsolatedAndCompiles()
    {
        var module = new PaymentsModule();
        Assert.Equal("payments", module.Name);
    }
}
