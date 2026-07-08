# Build with context = src/ (Deployment.Api references deployment/*, shared/Cicd.*,
# and Aspire/Cicd.Aspire.ServiceDefaults).
#   docker build --file src/deployment/Deployment.Api/Dockerfile -t deployment-api:latest ./src
#
# The image bundles the `aspirate` CLI + `kubectl` so the service can run `aspirate apply`
# to a cluster. It still needs, at runtime: a kubeconfig mounted in (KUBECONFIG /
# Deployment:Aspirate:Kubeconfig) whose API-server address is reachable FROM the container,
# and — for an insecure/HTTP Nexus — the cluster nodes configured for it (see
# samples/aspire-sample/kind-nexus-setup.sh). For kind, connect this container to the kind
# network and point the kubeconfig at the internal control-plane address.

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

# aspirate CLI (installed to a tool-path we copy into the runtime image).
RUN dotnet tool install --tool-path /aspirate Aspirate

# --- runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl for the HEALTHCHECK + the kubectl download; the aspnet base image doesn't include it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# aspirate CLI + kubectl so `aspirate apply` can deploy to a cluster.
# aspirate targets .NET 9; this image ships the .NET 10 runtime, so allow the tool to roll
# forward across the major version. (No-op for Deployment.Api, which matches 10 exactly.)
COPY --from=build /aspirate /opt/aspirate
ENV PATH="/opt/aspirate:${PATH}" \
    DOTNET_ROLL_FORWARD=Major
RUN ARCH="$(dpkg --print-architecture)" \
    && curl -fsSLo /usr/local/bin/kubectl \
       "https://dl.k8s.io/release/$(curl -fsSL https://dl.k8s.io/release/stable.txt)/bin/linux/${ARCH}/kubectl" \
    && chmod +x /usr/local/bin/kubectl

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=5 \
    CMD curl -fsS http://localhost:8080/ || exit 1

ENTRYPOINT ["dotnet", "Deployment.Api.dll"]
