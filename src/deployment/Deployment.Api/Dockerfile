# Build with context = src/ (Deployment.Api references deployment/*, shared/Cicd.*,
# and Aspire/Cicd.Aspire.ServiceDefaults).
#   docker build --file src/deployment/Deployment.Api/Dockerfile -t deployment-api:latest ./src
#
# Note: this image runs the API + the CI->deploy handoff consumer. Actually applying an
# Aspire app to a cluster (aspirate apply) additionally needs the `aspirate` CLI + a reachable
# kube context inside the container — extend this image (install the tool + mount a kubeconfig)
# or run the service on a host that has them.

# --- build stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files first for restore-layer caching.
COPY deployment/Deployment.Domain/*.csproj         deployment/Deployment.Domain/
COPY deployment/Deployment.Contracts/*.csproj      deployment/Deployment.Contracts/
COPY deployment/Deployment.Application/*.csproj    deployment/Deployment.Application/
COPY deployment/Deployment.Infrastructure/*.csproj deployment/Deployment.Infrastructure/
COPY deployment/Deployment.Api/*.csproj            deployment/Deployment.Api/
COPY shared/Cicd.IntegrationEvents/*.csproj        shared/Cicd.IntegrationEvents/
COPY shared/Cicd.Messaging/*.csproj                shared/Cicd.Messaging/
COPY Aspire/Cicd.Aspire.ServiceDefaults/*.csproj   Aspire/Cicd.Aspire.ServiceDefaults/
RUN dotnet restore deployment/Deployment.Api/Deployment.Api.csproj

# Copy the rest and publish.
COPY deployment/ deployment/
COPY shared/Cicd.IntegrationEvents/ shared/Cicd.IntegrationEvents/
COPY shared/Cicd.Messaging/ shared/Cicd.Messaging/
COPY Aspire/Cicd.Aspire.ServiceDefaults/ Aspire/Cicd.Aspire.ServiceDefaults/
RUN dotnet publish deployment/Deployment.Api/Deployment.Api.csproj \
    -c Release -o /app/publish --no-restore

# --- runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is needed by the HEALTHCHECK below; the aspnet base image doesn't include it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=5 \
    CMD curl -fsS http://localhost:8080/ || exit 1

ENTRYPOINT ["dotnet", "Deployment.Api.dll"]
