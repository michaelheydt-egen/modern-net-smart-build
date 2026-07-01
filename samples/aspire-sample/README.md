# samples/aspire-sample

A stock `dotnet new aspire-starter` app (AppHost + ApiService + Web + ServiceDefaults) used as a
fixture for the deployment service's **Aspire-application → Kubernetes** path (via Aspir8), and for
the per-service Kubernetes/Cloud Run paths. See the end-to-end walkthrough in
[docs/deployment/aspire-k8s-local-runbook.md](../../docs/deployment/aspire-k8s-local-runbook.md).

## Run it locally
```bash
dotnet build SampleApp.AppHost/SampleApp.AppHost.csproj      # builds the whole graph
dotnet run  --project SampleApp.AppHost                      # runs under the Aspire dashboard
```

## Publish it for the deployment platform (the CI role)
The service's Aspire deploy consumes two things: the images in Nexus, and the Kustomize-output
archive on the Nexus raw repo. `publish-to-nexus.sh` produces both:
```bash
NEXUS_PASS='<nexus-password>' ./publish-to-nexus.sh
# prints the manifest-source URL to register in web-admin → Deployment → Aspire apps
```
Prerequisites (one-time, per the runbook): `aspirate` on PATH, a Nexus `raw-hosted` repo, the target
namespace's image-pull secret, and the cluster node's insecure-registry trust for the Nexus host.

## Deploy it
In web-admin → **Deployment → Aspire apps**: register the app against the printed manifest-source URL
and a Kubernetes environment, then **Deploy**. The service fetches the archive, repoints the registry
host + digest-pins the images from Nexus, and runs `aspirate apply`.
