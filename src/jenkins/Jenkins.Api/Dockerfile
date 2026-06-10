# Build with context = src/ (the CI service references jenkins/* AND
# deployment/Deployment.Contracts).
#   docker build --file src/jenkins/Jenkins.Api/Dockerfile -t jenkins-api:latest ./src

# --- build stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files first for restore-layer caching.
COPY jenkins/Jenkins.Client/*.csproj                    jenkins/Jenkins.Client/
COPY jenkins/Jenkins.Orchestrator.Abstractions/*.csproj jenkins/Jenkins.Orchestrator.Abstractions/
COPY jenkins/Jenkins.Orchestrator/*.csproj              jenkins/Jenkins.Orchestrator/
COPY jenkins/Jenkins.Domain/*.csproj                    jenkins/Jenkins.Domain/
COPY jenkins/Jenkins.Contracts/*.csproj                 jenkins/Jenkins.Contracts/
COPY jenkins/Jenkins.Application/*.csproj               jenkins/Jenkins.Application/
COPY jenkins/Jenkins.Infrastructure/*.csproj            jenkins/Jenkins.Infrastructure/
COPY jenkins/Jenkins.Api/*.csproj                       jenkins/Jenkins.Api/
COPY deployment/Deployment.Contracts/*.csproj           deployment/Deployment.Contracts/
RUN dotnet restore jenkins/Jenkins.Api/Jenkins.Api.csproj

# Copy the rest and publish.
COPY jenkins/ jenkins/
COPY deployment/Deployment.Contracts/ deployment/Deployment.Contracts/
RUN dotnet publish jenkins/Jenkins.Api/Jenkins.Api.csproj \
    -c Release -o /app/publish --no-restore

# --- runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# SQLite DB lives on a mounted volume; ensure the non-root app user can write it.
RUN mkdir -p /app/data && chown -R app:app /app/data

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=5 \
    CMD curl -fsS http://localhost:8080/ || exit 1

ENTRYPOINT ["dotnet", "Jenkins.Api.dll"]
