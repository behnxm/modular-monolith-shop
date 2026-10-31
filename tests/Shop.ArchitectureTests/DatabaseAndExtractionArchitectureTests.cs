using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Infrastructure.Data;
using Shop.BuildingBlocks.Infrastructure.Modules;
using Shouldly;
using Xunit;

namespace Shop.ArchitectureTests;

public class DatabaseAndExtractionArchitectureTests
{
    [Fact]
    public void A8_ModuleDbContexts_AreInternal_DeriveModuleDbContext_AndDefaultSchemaMatchesModuleName()
    {
        for (var i = 0; i < ArchitectureTestContext.ModuleImplementationAssemblies.Count; i++)
        {
            var assembly = ArchitectureTestContext.ModuleImplementationAssemblies[i];
            var expectedSchema = ArchitectureTestContext.ModuleNames[i].ToLowerInvariant();

            var dbContextTypes = assembly.GetTypes()
                .Where(t => typeof(DbContext).IsAssignableFrom(t) && !t.IsAbstract)
                .ToList();

            dbContextTypes.Count.ShouldBe(1, $"Assembly '{assembly.GetName().Name}' should contain exactly one DbContext.");
            var dbContextType = dbContextTypes[0];

            dbContextType.IsNotPublic.ShouldBeTrue($"DbContext '{dbContextType.FullName}' must be internal.");
            typeof(ModuleDbContext).IsAssignableFrom(dbContextType).ShouldBeTrue(
                $"DbContext '{dbContextType.FullName}' must derive from ModuleDbContext.");

            // Verify default schema matches module name
            var moduleType = assembly.GetTypes().First(t => typeof(IModule).IsAssignableFrom(t));
            var moduleInstance = (IModule)Activator.CreateInstance(moduleType)!;
            moduleInstance.Name.ShouldBe(expectedSchema);
        }
    }

    [Fact]
    public void A9_HandlerConstructorDependencies_AreIsolated_ForExtractionReadiness()
    {
        var otherModuleImplAssemblies = ArchitectureTestContext.ModuleImplementationAssemblies;

        foreach (var assembly in ArchitectureTestContext.ModuleImplementationAssemblies)
        {
            var otherAssemblies = otherModuleImplAssemblies.Where(a => a != assembly).ToList();
            var handlerTypes = assembly.GetTypes()
                .Where(t => t.Name.EndsWith("Handler", StringComparison.Ordinal));

            foreach (var handler in handlerTypes)
            {
                foreach (var ctor in handler.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    foreach (var param in ctor.GetParameters())
                    {
                        var paramAssembly = param.ParameterType.Assembly;
                        otherAssemblies.Contains(paramAssembly).ShouldBeFalse(
                            $"Handler '{handler.FullName}' injects type '{param.ParameterType.FullName}' from foreign module implementation '{paramAssembly.GetName().Name}'. Only Contracts interfaces may be injected.");
                    }
                }
            }
        }
    }

    [Fact]
    public void A10_NoRoleLiterals_OrAuthorizeRoleAttributes()
    {
        foreach (var assembly in ArchitectureTestContext.ModuleImplementationAssemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                var authAttrs = type.GetCustomAttributes<AuthorizeAttribute>(true);
                foreach (var attr in authAttrs)
                {
                    string.IsNullOrWhiteSpace(attr.Roles).ShouldBeTrue(
                        $"Type '{type.FullName}' uses role-based authorization '[Authorize(Roles=...)]'. Must use policy-based authorization.");
                }

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    var methodAuthAttrs = method.GetCustomAttributes<AuthorizeAttribute>(true);
                    foreach (var attr in methodAuthAttrs)
                    {
                        string.IsNullOrWhiteSpace(attr.Roles).ShouldBeTrue(
                            $"Method '{type.FullName}.{method.Name}' uses role-based authorization '[Authorize(Roles=...)]'. Must use policy-based authorization.");
                    }
                }
            }
        }
    }
}
