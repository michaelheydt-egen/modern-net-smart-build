# Build with context = src/ (so the deployment projects resolve).
#   docker build --file src/deployment/Deployment.Api/Dockerfile -t deployment-api:latest ./src

# --- build stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files first for restore-layer caching.
COPY deployment/Deployment.Domain/*.csproj          deployment/Deployment.Domain/
COPY deployment/Deployment.Contracts/*.csproj       deployment/Deployment.Contracts/
COPY deployment/Deployment.Application/*.csproj     deployment/Deployment.Application/
COPY deployment/Deployment.Infrastructure/*.csproj  deployment/Deployment.Infrastructure/
COPY deployment/Deployment.Api/*.csproj             deployment/Deployment.Api/
RUN dotnet restore deployment/Deployment.Api/Deployment.Api.csproj

# Copy the rest and publish.
COPY deployment/ deployment/
RUN dotnet publish deployment/Deployment.Api/Deployment.Api.csproj \
    -c Release -o /app/publish --no-restore

# --- runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=6 \
    CMD curl -fsS http://localhost:8080/ || exit 1

ENTRYPOINT ["dotnet", "Deployment.Api.dll"]
