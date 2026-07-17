# Getting Started

How to run the platform locally and where the pieces live. For what it is and why, see the
[README](../README.md); for the full picture and diagrams, see [architecture.md](architecture.md).

## Service map

| Resource | Project | Role |
| --- | --- | --- |
| `jenkins-api` | `src/jenkins/Jenkins.*` | Orchestrates Jenkins jobs, polls builds, reconciles Nexus artifacts, raises CI events |
| `deployment-api` | `src/deployment/Deployment.*` | Services × Environments × Mappings, container inventory, deploy runs; Cloud Run + Aspire→K8s |
| `web-admin` | `src/web-admin/cicd.web.admin` | Blazor UI over both APIs (Jenkins, Nexus, SCA/SBOM, Deployment) |
| `sql` | SQL Server (container) | `JenkinsCi` + `Deployment` databases |
| `messaging` | RabbitMQ (container) | `ci.events` / `deployment.events` fanout channels |
| Jenkins + Nexus | standalone containers | pipeline execution + artifact storage (external to the AppHost) |

## Run it

Everything comes up from the **Aspire AppHost** — one command starts SQL Server, RabbitMQ,
`jenkins-api`, `deployment-api`, and `web-admin`:

```bash
dotnet run --project src/Aspire/Cicd.Aspire.Host
```

The console prints an **Aspire dashboard** URL (with a login token). Open it, wait for resources to go
green, then click the **web-admin** endpoint **from the dashboard** — that instance receives the
Aspire-assigned API URLs. (A web-admin started on its own falls back to a fixed port and can't find the
deployment-api.)

**Prerequisites:** Docker Desktop running (SQL Server + RabbitMQ run as containers); .NET 10 SDK. For the
Aspire→K8s deploy + preview features, enable Kubernetes in Docker Desktop (context `docker-desktop`).

### First-run secrets (once per machine)

Three AppHost parameters have no fallback — set them via user-secrets or `dotnet run` will block on a
prompt:

```bash
dotnet user-secrets set "Parameters:sql-password"     "<a-strong-Passw0rd!>" --project src/Aspire/Cicd.Aspire.Host
dotnet user-secrets set "Parameters:JenkinsUrl"       "http://localhost:8080" --project src/Aspire/Cicd.Aspire.Host
dotnet user-secrets set "Parameters:JenkinsApiToken"  "<token-or-placeholder>" --project src/Aspire/Cicd.Aspire.Host
```

- `sql-password` — SQL Server bakes this into its data volume on first init; pick one and keep it stable.
- `JenkinsUrl` / `JenkinsApiToken` — only exercised by CI build-sync; a placeholder is fine for
  deployment-only work.
- Nexus / crane / aspirate / kubeconfig parameters have sensible fallbacks — override only when used
  (see the runbooks below).

Databases auto-migrate on startup (`Database__AutoMigrate=true`); **never commit secrets** — use
environment variables / user-secrets.

## Repository layout

| Path | Contents |
| --- | --- |
| `src/Aspire/Cicd.Aspire.Host` | Aspire orchestration (the run entry point) |
| `src/jenkins/` | CI service (Domain / Application / Infrastructure / Api / Client / Orchestrator) |
| `src/deployment/` | Deployment service (Domain / Application / Infrastructure / Api / Contracts) |
| `src/web-admin/` | Blazor Server admin UI |
| `src/shared/Cicd.IntegrationEvents` | Cross-service event contracts |
| `jenkins/` | Jenkinsfiles (build / scan / publish) |
| `samples/aspire-sample/` | Sample Aspire app + `publish-to-nexus.sh` |
| `docs/` | Documentation |

## Common commands

```bash
dotnet run --project src/Aspire/Cicd.Aspire.Host          # run the whole stack
dotnet build src/deployment/deployment.sln                # build the deployment service
dotnet test                                                # run tests
# EF migration (deployment service):
dotnet ef migrations add <Name> --project src/deployment/Deployment.Infrastructure --startup-project src/deployment/Deployment.Api
```

## Deeper setup & runbooks

| Doc | What |
| --- | --- |
| [deployment/prerequisites.md](deployment/prerequisites.md) | GCP / crane / cluster prerequisites |
| [deployment/aspire-k8s-local-runbook.md](deployment/aspire-k8s-local-runbook.md) | Local docker-desktop cluster + Nexus setup for Aspire deploys |
| [demos/](demos/) | Live demo runbooks — blue-green, build pipeline, webhooks/ngrok, Kubernetes admin screens |
| [build-sync.md](build-sync.md) | How CI build/artifact reconciliation works |
| [sbom-setup.md](sbom-setup.md) | SBOM generation + Nexus storage |
