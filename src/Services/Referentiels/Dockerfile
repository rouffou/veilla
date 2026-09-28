# syntax=docker/dockerfile:1
# Image du service Référentiels — construite depuis la racine du dépôt :
#   docker build -f src/Services/Referentiels/Dockerfile -t sepp/referentiels .
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
COPY src/Services/Referentiels/src/Sepp.Referentiels.Domain/*.csproj src/Services/Referentiels/src/Sepp.Referentiels.Domain/
COPY src/Services/Referentiels/src/Sepp.Referentiels.Application/*.csproj src/Services/Referentiels/src/Sepp.Referentiels.Application/
COPY src/Services/Referentiels/src/Sepp.Referentiels.Adapters/*.csproj src/Services/Referentiels/src/Sepp.Referentiels.Adapters/
COPY src/Services/Referentiels/src/Sepp.Referentiels.Infrastructure/*.csproj src/Services/Referentiels/src/Sepp.Referentiels.Infrastructure/
RUN dotnet restore src/Services/Referentiels/src/Sepp.Referentiels.Infrastructure/Sepp.Referentiels.Infrastructure.csproj

COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Contracts/ src/Contracts/
COPY src/Services/Referentiels/src/ src/Services/Referentiels/src/
RUN dotnet publish src/Services/Referentiels/src/Sepp.Referentiels.Infrastructure/Sepp.Referentiels.Infrastructure.csproj \
      -c Release -o /app --no-restore /p:UseAppHost=false

# Image finale sans shell ni gestionnaire de paquets, utilisateur « app » (UID 1654).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Sepp.Referentiels.dll"]
