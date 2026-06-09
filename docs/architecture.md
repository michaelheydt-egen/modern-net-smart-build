# Architecture

Runtime architecture and CI/CD operations flow for the cicd workspace. Diagrams use [Mermaid](https://mermaid.js.org/) — they render inline in VSCode (Markdown Preview) and on GitHub.

## Runtime architecture

```mermaid
graph TB
    User[("👤 Operator<br/>(browser)")]

    subgraph WebUI["cicd.web.admin · Blazor Server · :8080"]
        Pages["Pages<br/>Orchestrator · Pipelines<br/>Nuget · Docker · Google"]
        Health["JenkinsHealthService<br/>(BackgroundService)"]
        Runner["PipelineRunner<br/>(singleton · in-mem ring buf)"]
        Orch["PipelineOrchestrator"]
        JC["JenkinsClient<br/>(HTTP + crumb)"]
        NC["NexusClient<br/>(HTTP Basic)"]
        GC["GcpClient<br/>(ADC)"]
    end

    subgraph Jenkins["Jenkins · :8080"]
        Jobs[("Pipelines<br/>cicd-build<br/>cicd-publish-*")]
    end

    subgraph Nexus["Nexus · :8081 REST · :8082 docker"]
        NugetRepo[("nuget-hosted")]
        DockerRepo[("docker-private")]
    end

    subgraph GCP["Google Cloud"]
        GAR[("Artifact Registry")]
        Run[("Cloud Run")]
    end

    User -->|"HTTPS<br/>SignalR"| Pages
    Pages --> Runner
    Pages --> JC
    Pages --> NC
    Pages --> GC
    Runner --> Orch
    Orch --> JC
    Health --> JC

    JC -->|"REST + crumb<br/>progressiveHtml stream"| Jobs
    NC -->|"/service/rest/v1<br/>components, repositories"| NugetRepo
    NC -->|"REST"| DockerRepo
    GC -->|"gRPC"| GAR
    GC -->|"gRPC"| Run

    Jobs -.->|"nuget push"| NugetRepo
    Jobs -.->|"docker push"| DockerRepo
    Jobs -.->|"docker push"| GAR
    Jobs -.->|"gcloud deploy"| Run
```

### Notes

- **Single-direction trust**: the WebUI initiates everything. Jenkins / Nexus / GCP never call back into the WebUI — state is pulled (poll / stream), not pushed.
- **In-memory orchestration**: `PipelineRunner` is a singleton — it survives page navigations but not WebUI restarts. Console logs live in a ~1 MB ring buffer per step.
- **Two Nexus ports**: REST API on `:8081`, Docker registry connector on `:8082`. Same hostname, different listeners. The WebUI only talks to the REST port; the Docker connector is used by Jenkins' `docker push`.
- **Credentials**: the WebUI reads Jenkins / Nexus credentials from environment variables (`Jenkins__ApiToken`, `Nexus__Password`); GCP uses Application Default Credentials. Nothing secret lives in `appsettings.json`.

## Operations — CI/CD pipeline chain

```mermaid
flowchart LR
    Build["cicd-build<br/><i>build .NET, produce<br/>build-info.json artifact</i>"]

    Nuget["cicd-publish-nexus-nuget<br/><i>nuget push → nuget-hosted</i>"]
    Docker["cicd-publish-nexus-docker<br/><i>docker push → docker-private</i>"]
    GAR["cicd-publish-gcp-gar<br/><i>docker push → Artifact Registry</i>"]
    GCR["cicd-publish-gcp-gcr<br/><i>deploy → Cloud Run</i>"]

    Build -->|"SOURCE_BUILD_NUMBER"| Nuget
    Build -->|"SOURCE_BUILD_NUMBER"| Docker
    Docker -->|"SOURCE_BUILD_NUMBER"| GAR
    GAR -->|"SOURCE_BUILD_NUMBER"| GCR

    classDef build fill:#1e3a5f,stroke:#4a7ab8,color:#fff;
    classDef nexus fill:#4a3a1e,stroke:#a07a3a,color:#fff;
    classDef gcp fill:#1e4a3a,stroke:#3a8a6a,color:#fff;
    class Build build;
    class Nuget,Docker nexus;
    class GAR,GCR gcp;
```

### Artifact forwarding

`cicd-build` archives `build-info.json` (package version, info version, git commit, build number). Every downstream step pulls that artifact from the upstream build using the Jenkins Copy Artifact plugin's `SpecificBuildSelector` (with `SOURCE_BUILD_NUMBER`) or `StatusBuildSelector` (last successful). The orchestrator's only job is to supply the right `SOURCE_BUILD_NUMBER` to each downstream invocation — the build-info JSON itself carries everything else.

### Parallelism

The chain is logically a DAG (the two `nexus-*` publishes share `cicd-build` as their input), but the orchestrator runs steps sequentially in declaration order. Parallel execution would need orchestrator changes — currently a single failed step stops the chain.

## Where things live

| Concern | Project |
| --- | --- |
| Blazor UI, pages, layout | `src/web-admin/cicd.web.admin` |
| Jenkins HTTP client | `src/jenkins/Jenkins.Client` |
| Pipeline orchestration | `src/jenkins/Jenkins.Orchestrator` (+ `.Abstractions`) |
| Jenkinsfiles | `jenkins/build/`, `jenkins/publish/nexus/{nuget,docker}/`, `jenkins/publish/gcp/{gar,gcr}/` |
| Pipeline definition (which job feeds which) | `Jenkins.Orchestrator/DefaultPipelines.cs` |
