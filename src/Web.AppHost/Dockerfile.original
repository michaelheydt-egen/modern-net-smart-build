# syntax=docker/dockerfile:1.7
# Build from this directory:
#   docker build -f Dockerfile -t web.apphost ..
# Or from the repo root:
#   docker build -f src/Web.AppHost/Dockerfile -t web.apphost src

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

COPY Web.AppHost/Web.AppHost.csproj                                 Web.AppHost/
COPY Model.Weather/Model.Weather.csproj                             Model.Weather/
COPY Web.Components.Weather/Web.Components.Weather.csproj           Web.Components.Weather/
COPY Web.Shared/Web.Shared.csproj                                   Web.Shared/
COPY Web.Shared.Assets/Web.Shared.Assets.csproj                     Web.Shared.Assets/
COPY Web.Shared.Components/Web.Shared.Components.csproj             Web.Shared.Components/
COPY Web.Shared.Components.Layout/Web.Shared.Components.Layout.csproj Web.Shared.Components.Layout/
COPY Web.Shared.Components.Pages/Web.Shared.Components.Pages.csproj Web.Shared.Components.Pages/

RUN dotnet restore Web.AppHost/Web.AppHost.csproj

COPY . .
RUN dotnet publish Web.AppHost/Web.AppHost.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /app/publish ./

USER app
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD wget --quiet --spider --tries=1 http://localhost:8080/ || exit 1

ENTRYPOINT ["dotnet", "Web.AppHost.dll"]
