# cicd-aspire-publish — Aspire app → Nexus (images + manifest artifact)

The Aspir8 counterpart of `cicd-build` + `cicd-publish-nexus-docker`, in one job. Given an Aspire
app repo it: builds + pushes a container image for **every** Aspire resource to the Nexus docker
registry (tagged `:{build#}-{commit}`, plus `:latest`, `:ci-{build#}`, `:{commit}`), runs
`aspirate generate` to produce the **Kustomize output**, and uploads that as
`aspirate-output.zip` to the Nexus **raw** repo. The printed manifest-source URL is what you register
in web-admin → **Deployment → Aspire apps**.

## One-time Jenkins setup
The platform triggers Jenkins jobs by name; it does not create them. Create a Jenkins **Pipeline** job
named **`cicd-aspire-publish`** using *Pipeline script from SCM* pointed at this file
(`jenkins/publish/aspire/Jenkinsfile`). Requirements (same as the other publish jobs):
- Build agent image `netsdk10:latest` with the docker socket mounted and on the `cicd-net` network.
- Jenkins credentials: `rhythm-docker` (Nexus docker registry) and `nexus-sbom` (Nexus REST/raw).
- A Nexus **raw hosted** repo (default `raw-hosted`); the docker registry's node must treat the Nexus
  host as insecure for the SDK/docker push (`DOTNET_CONTAINER_INSECURE_REGISTRIES` is set for it).

## Using it
1. web-admin → **CI → Repositories**: register the Aspire app repo (Git URL, default branch, base
   version). `GIT_URL`, `GIT_BRANCH`, and `BASE_VER` are injected into the job from the repository.
2. web-admin → **Orchestrator**: select the repo + the **"Aspire build"** pipeline (seeded
   automatically) → **Run**.
3. When it finishes, register the app against the printed manifest-source URL and deploy it.

Key parameters (defaults in the Jenkinsfile): `APPHOST_PROJECT` (blank = auto-discover the
`*.AppHost.csproj`), `NAMESPACE`, `NEXUS_DOCKER_HOST`, `NEXUS_RAW_REPO_URL`, `APP_NAME`.
