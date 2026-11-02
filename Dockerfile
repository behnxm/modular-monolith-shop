# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Copy directory configuration and solution files for layer-cached restore
COPY ["Directory.Build.props", "Directory.Packages.props", "global.json", "./"]
COPY ["ModularMonolithShop.slnx", "./"]

# Copy all csproj files
COPY ["src/BuildingBlocks/Shop.BuildingBlocks.Abstractions/Shop.BuildingBlocks.Abstractions.csproj", "src/BuildingBlocks/Shop.BuildingBlocks.Abstractions/"]
COPY ["src/BuildingBlocks/Shop.BuildingBlocks.Infrastructure/Shop.BuildingBlocks.Infrastructure.csproj", "src/BuildingBlocks/Shop.BuildingBlocks.Infrastructure/"]
COPY ["src/BuildingBlocks/Shop.BuildingBlocks.Testing/Shop.BuildingBlocks.Testing.csproj", "src/BuildingBlocks/Shop.BuildingBlocks.Testing/"]
COPY ["src/Host/Shop.Host/Shop.Host.csproj", "src/Host/Shop.Host/"]
COPY ["src/Modules/Catalog/Shop.Modules.Catalog/Shop.Modules.Catalog.csproj", "src/Modules/Catalog/Shop.Modules.Catalog/"]
COPY ["src/Modules/Catalog/Shop.Modules.Catalog.Contracts/Shop.Modules.Catalog.Contracts.csproj", "src/Modules/Catalog/Shop.Modules.Catalog.Contracts/"]
COPY ["src/Modules/Inventory/Shop.Modules.Inventory/Shop.Modules.Inventory.csproj", "src/Modules/Inventory/Shop.Modules.Inventory/"]
COPY ["src/Modules/Inventory/Shop.Modules.Inventory.Contracts/Shop.Modules.Inventory.Contracts.csproj", "src/Modules/Inventory/Shop.Modules.Inventory.Contracts/"]
COPY ["src/Modules/Ordering/Shop.Modules.Ordering/Shop.Modules.Ordering.csproj", "src/Modules/Ordering/Shop.Modules.Ordering/"]
COPY ["src/Modules/Ordering/Shop.Modules.Ordering.Contracts/Shop.Modules.Ordering.Contracts.csproj", "src/Modules/Ordering/Shop.Modules.Ordering.Contracts/"]
COPY ["src/Modules/Payments/Shop.Modules.Payments/Shop.Modules.Payments.csproj", "src/Modules/Payments/Shop.Modules.Payments/"]
COPY ["src/Modules/Payments/Shop.Modules.Payments.Contracts/Shop.Modules.Payments.Contracts.csproj", "src/Modules/Payments/Shop.Modules.Payments.Contracts/"]
COPY ["src/Modules/Notifications/Shop.Modules.Notifications/Shop.Modules.Notifications.csproj", "src/Modules/Notifications/Shop.Modules.Notifications/"]
COPY ["src/Modules/Notifications/Shop.Modules.Notifications.Contracts/Shop.Modules.Notifications.Contracts.csproj", "src/Modules/Notifications/Shop.Modules.Notifications.Contracts/"]
COPY ["tests/Shop.ArchitectureTests/Shop.ArchitectureTests.csproj", "tests/Shop.ArchitectureTests/"]
COPY ["tests/Shop.IntegrationTests/Shop.IntegrationTests.csproj", "tests/Shop.IntegrationTests/"]

# Restore packages
RUN dotnet restore src/Host/Shop.Host/Shop.Host.csproj

# Copy remaining source code
COPY src/ src/

# Build and publish host
RUN dotnet publish src/Host/Shop.Host/Shop.Host.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

# Switch to non-root user built into .NET container images
USER $APP_UID

COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .

HEALTHCHECK --interval=5s --timeout=3s --retries=5 --start-period=10s \
  CMD ["dotnet", "/app/Shop.Host.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "Shop.Host.dll"]
