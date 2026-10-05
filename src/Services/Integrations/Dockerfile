# syntax=docker/dockerfile:1
# Image du service Intégrations — construite depuis la racine du dépôt :
#   docker build -f src/Services/Integrations/Dockerfile -t sepp/integrations .
# CTR-01 image par service ; CTR-02 multi-stage ; CTR-03 base minimale (chiseled) ;
# CTR-04 utilisateur non root ; CTR-05 aucun secret ni configuration d'environnement.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restauration mise en cache : uniquement les fichiers de projet.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/BuildingBlocks/Sepp.BuildingBlocks.Domain/*.csproj src/BuildingBlocks/Sepp.BuildingBlocks.Domain/
COPY src/BuildingBlocks/Sepp.BuildingBlocks.Application/*.csproj src/BuildingBlocks/Sepp.BuildingBlocks.Application/
COPY src/BuildingBlocks/Sepp.BuildingBlocks.Infrastructure/*.csproj src/BuildingBlocks/Sepp.BuildingBlocks.Infrastructure/
COPY src/BuildingBlocks/Sepp.BuildingBlocks.Web/*.csproj src/BuildingBlocks/Sepp.BuildingBlocks.Web/
COPY src/Contracts/Sepp.Contracts/*.csproj src/Contracts/Sepp.Contracts/
COPY src/Services/Integrations/src/Sepp.Integrations.Domain/*.csproj src/Services/Integrations/src/Sepp.Integrations.Domain/
COPY src/Services/Integrations/src/Sepp.Integrations.Application/*.csproj src/Services/Integrations/src/Sepp.Integrations.Application/
COPY src/Services/Integrations/src/Sepp.Integrations.Adapters/*.csproj src/Services/Integrations/src/Sepp.Integrations.Adapters/
COPY src/Services/Integrations/src/Sepp.Integrations.Infrastructure/*.csproj src/Services/Integrations/src/Sepp.Integrations.Infrastructure/
RUN dotnet restore src/Services/Integrations/src/Sepp.Integrations.Infrastructure/Sepp.Integrations.Infrastructure.csproj

COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Contracts/ src/Contracts/
COPY src/Services/Integrations/src/ src/Services/Integrations/src/
RUN dotnet publish src/Services/Integrations/src/Sepp.Integrations.Infrastructure/Sepp.Integrations.Infrastructure.csproj \
      -c Release -o /app --no-restore /p:UseAppHost=false

# Image finale sans shell ni gestionnaire de paquets, utilisateur « app » (UID 1654).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Sepp.Integrations.dll"]
