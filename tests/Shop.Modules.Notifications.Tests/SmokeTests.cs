using Xunit;

namespace Shop.Modules.Notifications.Tests;

public class SmokeTests
{
    [Fact]
    public void ModuleAssembly_IsIsolatedAndCompiles()
    {
        var module = new NotificationsModule();
        Assert.Equal("notifications", module.Name);
    }
}
