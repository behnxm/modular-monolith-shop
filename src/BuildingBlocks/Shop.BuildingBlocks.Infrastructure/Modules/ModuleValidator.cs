namespace Shop.BuildingBlocks.Infrastructure.Modules;

public static class ModuleValidator
{
    public static void ValidateDependencies(IReadOnlyCollection<IModule> enabledModules)
    {
        ArgumentNullException.ThrowIfNull(enabledModules);

        var enabledNamesSet = new HashSet<string>(
            enabledModules.Select(m => m.Name.ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var module in enabledModules)
        {
            foreach (var required in module.RequiredModules)
            {
                if (!enabledNamesSet.Contains(required.ToLowerInvariant()))
                {
                    throw new InvalidOperationException(
                        $"Module '{module.Name}' requires module '{required}', but '{required}' is not enabled in configuration 'Modules:Enabled'.");
                }
            }
        }
    }
}
