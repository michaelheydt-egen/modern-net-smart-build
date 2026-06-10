# cicd-* jobs as code (Job DSL seed)

[`cicd-jobs.groovy`](cicd-jobs.groovy) defines the three orchestrator jobs as a
[Job DSL](https://plugins.jenkins.io/job-dsl/) script so the controller can
(re)create them reproducibly instead of hand-clicking in the UI:

| Job | Type | Jenkinsfile | Repo it builds |
| --- | --- | --- | --- |
| `cicd-build` | parameterized Pipeline | `jenkins/build/Jenkinsfile` | **caller-supplied `GIT_URL`** (lightweight Jenkinsfile fetch) |
| `cicd-publish-nexus-nuget` | parameterized Pipeline | `jenkins/publish/nexus/nuget/Jenkinsfile` | this repo (full checkout — packs `BUILD_FILE`) |
| `cicd-publish-nexus-docker` | parameterized Pipeline | `jenkins/publish/nexus/docker/Jenkinsfile` | this repo (full checkout) |

The DSL **declares each job's parameters** so the orchestrator's first
`POST /job/<name>/buildWithParameters?GIT_URL=…` succeeds before any run has
executed the Jenkinsfile's declarative `parameters {}` block. The Jenkinsfile
stays the source of truth for parameters after the first run.

## Prerequisites

- **Job DSL** plugin installed.
- A credentials entry for cloning this (pipeline-definition) repo if it's
  private — pass its id via `PIPELINE_REPO_CREDENTIAL_ID` (see overrides below).
- The `cicd-build` clone of a private *target* repo uses a separate credential,
  supplied per-build via the job's own `GIT_CREDENTIALS_ID` parameter.

## Option A — Seed job (Process Job DSLs)

1. **New Item → Freestyle project**, name `seed`.
2. **Source Code Management → Git**: URL of this repo, branch `main` (+ credentials
   if private). This puts `jenkins/jobs/*.groovy` on the workspace filesystem.
3. **Build Steps → Process Job DSLs**:
   - *Look on Filesystem* → **DSL Scripts**: `jenkins/jobs/cicd-jobs.groovy`
   - Action for removed jobs: *Ignore* (or *Disable*).
4. *(optional)* **Build → Inject environment variables** (or string parameters) to
   override the defaults:
   - `PIPELINE_REPO_URL` (default `https://github.com/mhdm-egen/net-jenkins-gcr.git`)
   - `PIPELINE_REPO_BRANCH` (default `main`)
   - `PIPELINE_REPO_CREDENTIAL_ID` (default empty)
5. **Save → Build**. The three `cicd-*` jobs are created. Re-run after editing the
   DSL to apply changes.

> First run may need DSL **script approval** (Manage Jenkins → In-process Script
> Approval) unless the seed job runs as an admin / the DSL is on the approved list.

## Option B — JCasC bootstrap

If you drive the controller with [Configuration as Code](https://plugins.jenkins.io/configuration-as-code/),
have JCasC create the seed job and run it on boot:

```yaml
jobs:
  - script: >
      job('seed') {
        scm {
          git {
            remote { url('https://github.com/mhdm-egen/net-jenkins-gcr.git') }
            branch('main')
          }
        }
        steps {
          dsl {
            external('jenkins/jobs/cicd-jobs.groovy')
            removeAction('IGNORE')
          }
        }
      }
```

Trigger `seed` once (JCasC doesn't auto-run jobs) — e.g. build it from the UI or
`java -jar jenkins-cli.jar build seed`. Everything after that is in `cicd-jobs.groovy`.

## After seeding

- Open **`cicd-build` → Build with Parameters**, set a real `GIT_URL`, and run once.
- The orchestrator (web-admin → **Orchestrator**) can now pick a repository +
  pipeline and trigger `cicd-build` with `GIT_URL`/`GIT_BRANCH` injected.

## Follow-up: repo-agnostic publish

The publish jobs currently pack/build the source in *this* repo (`BUILD_FILE`,
default `src/app/cicd.sln`). To make them build an arbitrary `GIT_URL` like
`cicd-build` does, give their Jenkinsfiles the same treatment —
`skipDefaultCheckout(true)` + a `Checkout` stage that clones `GIT_URL@GIT_BRANCH`
(or the commit recorded in `build-info.json`) — then add `GIT_URL`/`GIT_BRANCH`
params here and flip their `lightweight` flag to `true`.
