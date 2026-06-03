# SBOM publishing — one-time setup

`cicd-build` produces three security artifacts on every build:

| File | Tool | Purpose |
| --- | --- | --- |
| `bom.json` | `dotnet CycloneDX` | Plain CycloneDX SBOM of the solution's NuGet graph (direct + transitive) |
| `vulnerabilities.json` | `dotnet list package --vulnerable` | Flat list of vulnerable packages per the NuGet advisory DB |
| `bom-vex.json` | `trivy sbom` | CycloneDX SBOM **with vulnerabilities embedded** (VEX-style) — feeds Dependency-Track, OWASP DT, etc. |

`cicd-publish-nexus-nuget` pushes all three into a Nexus **raw (hosted)** repo alongside the `.nupkg` push. Two pieces of one-time setup before the first run.

## 1. Create the Nexus `sboms` raw repo

In the Nexus UI:

1. **Settings (gear) → Repository → Repositories → Create repository**
2. Pick **raw (hosted)**
3. Name it `sboms` (matches the default in `SBOM_NEXUS_REPO_URL` — change both if you pick a different name)
4. Blob store: any (default is fine)
5. Deployment policy: **Allow redeploy** (lets a republish overwrite the previous SBOM if you re-cut the same version)

Or via REST:

```bash
curl -u admin:$NEXUS_PASS -X POST -H "Content-Type: application/json" \
  http://localhost:8081/service/rest/v1/repositories/raw/hosted \
  -d '{
    "name": "sboms",
    "online": true,
    "storage": { "blobStoreName": "default", "strictContentTypeValidation": false, "writePolicy": "ALLOW" }
  }'
```

After publishing, each build's artifacts live at:

```text
http://nexus:8081/repository/sboms/sbom/<PACKAGE_VERSION>/bom.json
http://nexus:8081/repository/sboms/sbom/<PACKAGE_VERSION>/vulnerabilities.json
http://nexus:8081/repository/sboms/sbom/<PACKAGE_VERSION>/bom-vex.json
```

It will show up in the Nexus UI's Browse view under `sboms`, so the existing **Nexus → Nuget Repo / Docker Repo** menu pattern can be extended with an `Sbom Repo` deep link if useful.

## 2. Add the Jenkins credential

The upload uses HTTP Basic against the Nexus REST API, so it needs a username + password / token (the **REST API password**, not the NuGet API key — see the existing `Nexus__Password` note).

In Jenkins: **Manage Jenkins → Credentials → System → Global → Add Credentials**

- Kind: **Username with password**
- ID: `nexus-rest` (matches `SBOM_NEXUS_CREDENTIAL_ID` default in the publish-nuget Jenkinsfile)
- Username: e.g. `admin`
- Password: the Nexus REST API password / user-token pass code

## Toggles

Optional knobs on `cicd-build`:

| Parameter | Default | Notes |
| --- | --- | --- |
| `CYCLONEDX_TOOL_VERSION` | `5.4.0` | Pinned dotnet CycloneDX tool version. Bump after testing locally. |
| `TRIVY_VERSION` | `v0.55.0` | Fallback Trivy version downloaded into the workspace **only when the build image doesn't ship one**. The current `netsdk10` image bakes Trivy in via Aqua's apt repo ([devops/Dockerfile-build](../devops/Dockerfile-build)), so this param is unused on up-to-date images. |
| `FAIL_ON_SEVERITY` | `none` | Set to `high` or `critical` to wedge the build when vulns at that severity (or worse) are present. (Currently gates on `vulnerabilities.json` from the dotnet-native scan; the trivy enrichment is informational.) |

### Trivy DB cache (recommended)

Trivy downloads a ~50–100 MB vulnerability DB on first run. Without a cache, every build re-downloads it. To cache across builds, append this to `BUILD_CONTAINER_ARGS` in the Jenkins job config:

```text
-v /tmp/trivy-cache:/root/.cache/trivy
```

The Trivy **binary** itself is baked into [devops/Dockerfile-build](../devops/Dockerfile-build) via Aqua's apt repo — `apt-get upgrade trivy` (or a fresh image build) pulls future versions. The Jenkinsfile prefers the baked-in binary and falls back to a workspace-local download only if the image doesn't have it.

## Verifying

After a build run:

```bash
# build-side
curl -u $REST_USER:$REST_PASS \
  "http://localhost:8081/service/rest/v1/components?repository=sboms" | jq '.items[].name'

# publish-side (one specific version)
curl -u $REST_USER:$REST_PASS -O \
  http://localhost:8081/repository/sboms/sbom/1.0.0-ci.42.gabc1234/bom.json

# vuln-enriched view (CycloneDX with VEX) — ready to ingest into Dependency-Track
curl -u $REST_USER:$REST_PASS -O \
  http://localhost:8081/repository/sboms/sbom/1.0.0-ci.42.gabc1234/bom-vex.json
```
