# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -fsS http://localhost:8080/alive || exit 1


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/app/Web.AppHost/Web.AppHost.csproj", "src/Web.AppHost/"]
COPY ["src/app/Aspire/Aspire.ServiceDefaults/Aspire.ServiceDefaults.csproj", "src/Aspire/Aspire.ServiceDefaults/"]
COPY ["src/app/Model.Weather/Model.Weather.csproj", "src/Model.Weather/"]
COPY ["src/app/Web.Components.Weather/Web.Components.Weather.csproj", "src/Web.Components.Weather/"]
COPY ["src/app/Web.Shared.Components.Layout/Web.Shared.Components.Layout.csproj", "src/Web.Shared.Components.Layout/"]
COPY ["src/app/Web.Shared.Components.Pages/Web.Shared.Components.Pages.csproj", "src/Web.Shared.Components.Pages/"]
COPY ["src/app/Web.Shared/Web.Shared.csproj", "src/Web.Shared/"]
COPY ["src/app/Web.Shared.Assets/Web.Shared.Assets.csproj", "src/Web.Shared.Assets/"]
COPY ["src/app/Web.Shared.Components/Web.Shared.Components.csproj", "src/Web.Shared.Components/"]
RUN pwd
RUN ls ./src
RUN dotnet restore "./src/Web.AppHost/Web.AppHost.csproj"
COPY . .
WORKDIR "/src/src/Web.AppHost"
RUN dotnet build "./Web.AppHost.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Web.AppHost.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Web.AppHost.dll"]