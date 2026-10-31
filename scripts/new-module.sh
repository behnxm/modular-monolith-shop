#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 2 ]; then
    echo "Usage: $0 <ModuleName> <schema>"
    echo "Example: $0 Catalog catalog"
    exit 1
fi

NAME="$1"
SCHEMA="$(echo "$2" | tr '[:upper:]' '[:lower:]')"

IMPL_DIR="src/Modules/${NAME}/Shop.Modules.${NAME}"
CONTRACTS_DIR="src/Modules/${NAME}/Shop.Modules.${NAME}.Contracts"
TESTS_DIR="tests/Shop.Modules.${NAME}.Tests"
DOC_FILE="docs/modules/${SCHEMA}.md"

if [ -d "$IMPL_DIR" ]; then
    echo "Error: Module directory '$IMPL_DIR' already exists. Refusing to overwrite."
    exit 1
fi

echo "Scaffolding module '${NAME}' (schema: '${SCHEMA}')..."

# Create directory structure
mkdir -p "${IMPL_DIR}/Domain"
mkdir -p "${IMPL_DIR}/Application"
mkdir -p "${IMPL_DIR}/IntegrationEventHandlers"
mkdir -p "${IMPL_DIR}/PublicApi"
mkdir -p "${IMPL_DIR}/Infrastructure/Migrations"
mkdir -p "${IMPL_DIR}/Endpoints"

mkdir -p "${CONTRACTS_DIR}/IntegrationEvents/V1"
mkdir -p "${TESTS_DIR}"
mkdir -p "docs/modules"

# 1. Contracts project
cat <<EOF > "${CONTRACTS_DIR}/Shop.Modules.${NAME}.Contracts.csproj"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\BuildingBlocks\Shop.BuildingBlocks.Abstractions\Shop.BuildingBlocks.Abstractions.csproj" />
  </ItemGroup>
</Project>
EOF

# 2. Implementation project
cat <<EOF > "${IMPL_DIR}/Shop.Modules.${NAME}.csproj"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\BuildingBlocks\Shop.BuildingBlocks.Infrastructure\Shop.BuildingBlocks.Infrastructure.csproj" />
    <ProjectReference Include="..\Shop.Modules.${NAME}.Contracts\Shop.Modules.${NAME}.Contracts.csproj" />
  </ItemGroup>
</Project>
EOF

# 3. Test project
cat <<EOF > "${TESTS_DIR}/Shop.Modules.${NAME}.Tests.csproj"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="Shouldly" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\BuildingBlocks\Shop.BuildingBlocks.Testing\Shop.BuildingBlocks.Testing.csproj" />
    <ProjectReference Include="..\..\src\Modules\\${NAME}\Shop.Modules.${NAME}\Shop.Modules.${NAME}.csproj" />
    <ProjectReference Include="..\..\src\Modules\\${NAME}\Shop.Modules.${NAME}.Contracts\Shop.Modules.${NAME}.Contracts.csproj" />
  </ItemGroup>
</Project>
EOF

# 4. AssemblyInfo.cs (InternalsVisibleTo only)
cat <<EOF > "${IMPL_DIR}/AssemblyInfo.cs"
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Shop.Modules.${NAME}.Tests")]
EOF

# 5. DbContext and DesignTimeFactory
cat <<EOF > "${IMPL_DIR}/Infrastructure/${NAME}DbContext.cs"
using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Infrastructure.Data;

namespace Shop.Modules.${NAME}.Infrastructure;

internal sealed class ${NAME}DbContext(DbContextOptions<${NAME}DbContext> options)
    : ModuleDbContext(options, "${SCHEMA}")
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(${NAME}DbContext).Assembly);
    }
}
EOF

cat <<EOF > "${IMPL_DIR}/Infrastructure/${NAME}DbContextFactory.cs"
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shop.Modules.${NAME}.Infrastructure;

internal sealed class ${NAME}DbContextFactory : IDesignTimeDbContextFactory<${NAME}DbContext>
{
    public ${NAME}DbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<${NAME}DbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=shop;Username=shop_migrator;Password=migrator_secure_password_2026",
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "${SCHEMA}"));

        return new ${NAME}DbContext(optionsBuilder.Options);
    }
}
EOF

# 6. Endpoints skeleton
cat <<EOF > "${IMPL_DIR}/Endpoints/${NAME}Endpoints.cs"
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Shop.Modules.${NAME}.Endpoints;

internal static class ${NAME}Endpoints
{
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("api/v1/${SCHEMA}")
            .WithTags("${NAME}");
    }
}
EOF

# 7. Module class (the ONLY public class in implementation assembly)
cat <<EOF > "${IMPL_DIR}/${NAME}Module.cs"
using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shop.BuildingBlocks.Infrastructure.Data;
using Shop.BuildingBlocks.Infrastructure.Health;
using Shop.BuildingBlocks.Infrastructure.Modules;
using Shop.BuildingBlocks.Infrastructure.Outbox;
using Shop.Modules.${NAME}.Endpoints;
using Shop.Modules.${NAME}.Infrastructure;

namespace Shop.Modules.${NAME};

public sealed class ${NAME}Module : IModule
{
    public string Name => "${SCHEMA}";
    public Assembly ContractsAssembly => typeof(Shop.Modules.${NAME}.Contracts.Marker).Assembly;
    public IReadOnlyCollection<string> RequiredModules => Array.Empty<string>();

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<${NAME}DbContext>(configuration, Name);
        services.AddHostedService<OutboxProcessor<${NAME}DbContext>>();
        services.AddHealthChecks()
            .AddCheck<OutboxHealthCheck<${NAME}DbContext>>($"outbox_{Name}");
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ${NAME}Endpoints.MapEndpoints(endpoints);
    }

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<${NAME}DbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }
}
EOF

# 8. Contracts marker type
cat <<EOF > "${CONTRACTS_DIR}/Marker.cs"
namespace Shop.Modules.${NAME}.Contracts;

public static class Marker;
EOF

# 9. Test project smoke test
cat <<EOF > "${TESTS_DIR}/SmokeTests.cs"
using Xunit;

namespace Shop.Modules.${NAME}.Tests;

public class SmokeTests
{
    [Fact]
    public void ModuleAssembly_IsIsolatedAndCompiles()
    {
        var module = new ${NAME}Module();
        Assert.Equal("${SCHEMA}", module.Name);
    }
}
EOF

# 10. Module documentation from template
cat <<EOF > "$DOC_FILE"
# ${NAME} Module

## What it owns

## Public API

## Events it publishes

## Events it consumes

## Endpoints and policies

## Rules and invariants

## How it is tested
EOF

# 11. Add projects to solution
dotnet sln ModularMonolithShop.slnx add "${CONTRACTS_DIR}/Shop.Modules.${NAME}.Contracts.csproj"
dotnet sln ModularMonolithShop.slnx add "${IMPL_DIR}/Shop.Modules.${NAME}.csproj"
dotnet sln ModularMonolithShop.slnx add "${TESTS_DIR}/Shop.Modules.${NAME}.Tests.csproj"

# 12. Add project reference to Host
dotnet add src/Host/Shop.Host/Shop.Host.csproj reference "${IMPL_DIR}/Shop.Modules.${NAME}.csproj"

echo "Module '${NAME}' scaffolded successfully."
