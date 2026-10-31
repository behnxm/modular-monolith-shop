using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace Shop.ArchitectureTests;

public class ModuleLayeringTests
{
    [Fact]
    public void A5_Domain_DoesNotDependOn_Infrastructure_Application_Or_Endpoints()
    {
        foreach (var assembly in ArchitectureTestContext.ModuleImplementationAssemblies)
        {
            var modulePrefix = assembly.GetName().Name!;

            var result = Types.InAssembly(assembly)
                .That()
                .ResideInNamespace($"{modulePrefix}.Domain")
                .ShouldNot()
                .HaveDependencyOnAny(
                    $"{modulePrefix}.Infrastructure",
                    $"{modulePrefix}.Application",
                    $"{modulePrefix}.Endpoints")
                .GetResult();

            result.IsSuccessful.ShouldBeTrue(
                $"Domain in {modulePrefix} must not depend on Infrastructure, Application, or Endpoints: {string.Join(", ", result.FailingTypes?.Select(t => t.FullName) ?? Array.Empty<string>())}");
        }
    }

    [Fact]
    public void A5_Application_DoesNotDependOn_Endpoints()
    {
        foreach (var assembly in ArchitectureTestContext.ModuleImplementationAssemblies)
        {
            var modulePrefix = assembly.GetName().Name!;

            var result = Types.InAssembly(assembly)
                .That()
                .ResideInNamespace($"{modulePrefix}.Application")
                .ShouldNot()
                .HaveDependencyOnAny($"{modulePrefix}.Endpoints")
                .GetResult();

            result.IsSuccessful.ShouldBeTrue(
                $"Application in {modulePrefix} must not depend on Endpoints.");
        }
    }

    [Fact]
    public void A5_IntegrationEventHandlers_DoNotDependOn_Endpoints()
    {
        foreach (var assembly in ArchitectureTestContext.ModuleImplementationAssemblies)
        {
            var modulePrefix = assembly.GetName().Name!;

            var result = Types.InAssembly(assembly)
                .That()
                .ResideInNamespace($"{modulePrefix}.IntegrationEventHandlers")
                .ShouldNot()
                .HaveDependencyOnAny($"{modulePrefix}.Endpoints")
                .GetResult();

            result.IsSuccessful.ShouldBeTrue(
                $"IntegrationEventHandlers in {modulePrefix} must not depend on Endpoints.");
        }
    }

    [Fact]
    public void A5_Endpoints_DoNotReference_DbContext()
    {
        foreach (var assembly in ArchitectureTestContext.ModuleImplementationAssemblies)
        {
            var endpointTypes = assembly.GetTypes()
                .Where(t => t.Namespace?.Contains(".Endpoints", StringComparison.Ordinal) == true);

            foreach (var type in endpointTypes)
            {
                var methods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance);
                foreach (var method in methods)
                {
                    var parameters = method.GetParameters();
                    foreach (var param in parameters)
                    {
                        typeof(DbContext).IsAssignableFrom(param.ParameterType).ShouldBeFalse(
                            $"Endpoint '{type.FullName}.{method.Name}' directly references DbContext parameter '{param.ParameterType.Name}'. Endpoints must never touch DbContext.");
                    }
                }
            }
        }
    }

    [Fact]
    public void A5_Handlers_DoNotReference_HttpContext()
    {
        foreach (var assembly in ArchitectureTestContext.ModuleImplementationAssemblies)
        {
            var handlerTypes = assembly.GetTypes()
                .Where(t => t.Name.EndsWith("Handler", StringComparison.Ordinal));

            foreach (var type in handlerTypes)
            {
                var ctors = type.GetConstructors();
                foreach (var ctor in ctors)
                {
                    foreach (var param in ctor.GetParameters())
                    {
                        (param.ParameterType == typeof(HttpContext) || param.ParameterType == typeof(IHttpContextAccessor)).ShouldBeFalse(
                            $"Handler '{type.FullName}' has dependency on '{param.ParameterType.Name}'. Handlers must not touch HttpContext.");
                    }
                }

                var methods = type.GetMethods();
                foreach (var method in methods)
                {
                    foreach (var param in method.GetParameters())
                    {
                        (param.ParameterType == typeof(HttpContext) || param.ParameterType == typeof(IHttpContextAccessor)).ShouldBeFalse(
                            $"Handler '{type.FullName}.{method.Name}' has parameter '{param.ParameterType.Name}'. Handlers must receive domain parameters or ICurrentUser.");
                    }
                }
            }
        }
    }
}
