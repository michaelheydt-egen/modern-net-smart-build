# Build with context = src/ so both the jenkins/* project refs AND the
# deployment/Deployment.Contracts ref are visible.
#   docker build --file src/web-admin/cicd.web.admin/Dockerfile -t cicd-web-admin:latest ./src

# --- build stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files first for layer caching of `dotnet restore`.
COPY jenkins/Jenkins.Client/*.csproj                        jenkins/Jenkins.Client/
COPY jenkins/Jenkins.Orchestrator.Abstractions/*.csproj     jenkins/Jenkins.Orchestrator.Abstractions/
COPY jenkins/Jenkins.Orchestrator/*.csproj                  jenkins/Jenkins.Orchestrator/
COPY deployment/Deployment.Contracts/*.csproj               deployment/Deployment.Contracts/
COPY web-admin/cicd.web.admin/*.csproj                      web-admin/cicd.web.admin/
RUN dotnet restore web-admin/cicd.web.admin/cicd.web.admin.csproj

# Copy the rest of the source and publish.
COPY jenkins/Jenkins.Client/                     jenkins/Jenkins.Client/
COPY jenkins/Jenkins.Orchestrator.Abstractions/  jenkins/Jenkins.Orchestrator.Abstractions/
COPY jenkins/Jenkins.Orchestrator/               jenkins/Jenkins.Orchestrator/
COPY deployment/Deployment.Contracts/            deployment/Deployment.Contracts/
COPY web-admin/cicd.web.admin/                   web-admin/cicd.web.admin/
RUN dotnet publish web-admin/cicd.web.admin/cicd.web.admin.csproj \
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

ENTRYPOINT ["dotnet", "cicd.web.admin.dll"]
