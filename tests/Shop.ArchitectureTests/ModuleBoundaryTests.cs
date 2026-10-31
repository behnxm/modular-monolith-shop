using System.Reflection;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace Shop.ArchitectureTests;

public class ModuleBoundaryTests
{
    [Fact]
    public void A1_ModuleImplementations_DoNotReference_OtherModuleImplementations()
    {
        foreach (var assembly in ArchitectureTestContext.ModuleImplementationAssemblies)
        {
            var currentModuleName = assembly.GetName().Name!;
            var otherModuleImplementations = ArchitectureTestContext.ModuleImplementationAssemblies
                .Where(a => a != assembly)
                .Select(a => a.GetName().Name!)
                .ToArray();

            var referencedAssemblies = assembly.GetReferencedAssemblies()
                .Select(r => r.Name!)
                .ToList();

            foreach (var otherModule in otherModuleImplementations)
            {
                referencedAssemblies.ShouldNotContain(
                    otherModule,
                    $"Module implementation '{currentModuleName}' must not reference other module implementation '{otherModule}'.");
            }
        }
    }

    [Fact]
    public void A2_ContractsAssemblies_ReferenceOnly_AbstractionsAndBCL()
    {
        var allowedPrefixes = new[]
        {
            "System",
            "Microsoft.",
            "Shop.BuildingBlocks.Abstractions",
            "mscorlib",
            "netstandard"
        };

        foreach (var assembly in ArchitectureTestContext.ModuleContractsAssemblies)
        {
            var references = assembly.GetReferencedAssemblies();
            foreach (var reference in references)
            {
                var refName = reference.Name!;
                var isAllowed = allowedPrefixes.Any(p => refName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
                isAllowed.ShouldBeTrue(
                    $"Contracts assembly '{assembly.GetName().Name}' references '{refName}', but is only allowed to reference Abstractions and BCL.");
            }
        }
    }

    [Fact]
    public void A3_BuildingBlocks_DoNotReference_ModuleAssemblies()
    {
        var allModuleAssemblyNames = ArchitectureTestContext.ModuleImplementationAssemblies
            .Concat(ArchitectureTestContext.ModuleContractsAssemblies)
            .Select(a => a.GetName().Name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in ArchitectureTestContext.BuildingBlocksAssemblies)
        {
            var referenced = assembly.GetReferencedAssemblies()
                .Select(r => r.Name!)
                .ToList();

            foreach (var refName in referenced)
            {
                allModuleAssemblyNames.Contains(refName).ShouldBeFalse(
                    $"BuildingBlocks assembly '{assembly.GetName().Name}' must not reference module assembly '{refName}'.");
            }
        }
    }

    [Fact]
    public void A4_ModuleImplementation_OnlyExposes_IModuleClass_Publicly()
    {
        foreach (var assembly in ArchitectureTestContext.ModuleImplementationAssemblies)
        {
            var publicTypes = assembly.GetExportedTypes();
            foreach (var type in publicTypes)
            {
                // The only allowed public types are the MModule class
                // and the design-time DbContextFactory if permitted by ADR-0002
                var isAllowed = type.Name.EndsWith("Module", StringComparison.Ordinal) ||
                                type.Name.EndsWith("DbContextFactory", StringComparison.Ordinal);

                isAllowed.ShouldBeTrue(
                    $"Assembly '{assembly.GetName().Name}' exposes public type '{type.FullName}'. All types except IModule must be internal.");
            }
        }
    }
}
