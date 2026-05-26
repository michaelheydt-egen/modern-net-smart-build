# Build with context = src/jenkins/ so all sibling project refs are visible.
#   docker build --file src/jenkins/Jenkins.WebUI/Dockerfile -t jenkins-webui:latest ./src/jenkins

# --- build stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files first for layer caching of `dotnet restore`.
COPY Jenkins.Client/*.csproj                       Jenkins.Client/
COPY Jenkins.Orchestrator.Abstractions/*.csproj    Jenkins.Orchestrator.Abstractions/
COPY Jenkins.Orchestrator/*.csproj                 Jenkins.Orchestrator/
COPY Jenkins.WebUI/*.csproj                        Jenkins.WebUI/
RUN dotnet restore Jenkins.WebUI/Jenkins.WebUI.csproj

# Copy the rest of the source and publish.
COPY Jenkins.Client/                       Jenkins.Client/
COPY Jenkins.Orchestrator.Abstractions/    Jenkins.Orchestrator.Abstractions/
COPY Jenkins.Orchestrator/                 Jenkins.Orchestrator/
COPY Jenkins.WebUI/                        Jenkins.WebUI/
RUN dotnet publish Jenkins.WebUI/Jenkins.WebUI.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# --- runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is needed by the HEALTHCHECK below. aspnet base image doesn't include it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# The aspnet base image ships a non-root `app` user (uid 1654).
USER app

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -fsS http://localhost:8080/alive || exit 1

ENTRYPOINT ["dotnet", "Jenkins.WebUI.dll"]
