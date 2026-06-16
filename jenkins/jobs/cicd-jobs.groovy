// =============================================================================
// Job DSL seed for the cicd-* pipeline jobs.
//
// Run by a "seed" job (Process Job DSLs build step) that has first checked out
// this repo, or by JCasC's jobs: bootstrap. See jenkins/jobs/README.md.
//
// All three jobs load their Jenkinsfile from THIS repo (the pipeline-definition
// repo), not from the application being built. All three are repo-agnostic:
//
//   * cicd-build              -> lightweight Jenkinsfile fetch. The Jenkinsfile
//                                sets skipDefaultCheckout(true) and clones the
//                                caller-supplied GIT_URL itself.
//   * cicd-publish-nexus-*    -> lightweight Jenkinsfile fetch. The Jenkinsfiles
//                                set skipDefaultCheckout(true), then clone the app
//                                repo recorded in the upstream build-info.json
//                                (gitUrl), pinned to gitCommitHash, and pack/build
//                                that exact source — so they publish any repo, not
//                                just this monorepo (and exactly the built commit).
//                                Private repos: pass GIT_CREDENTIALS_ID.
//
// Declaring the parameters here pre-registers them so the orchestrator's first
// POST /buildWithParameters succeeds before any run has executed the declarative
// `parameters {}` block. The Jenkinsfile remains the source of truth thereafter.
// =============================================================================

// Where the Jenkinsfiles live. Override by binding these in the seed job
// (e.g. as String parameters / env) — otherwise the defaults below are used.
def pipelineRepo   = binding.hasVariable('PIPELINE_REPO_URL')           ? PIPELINE_REPO_URL           : 'https://github.com/mhdm-egen/net-jenkins-gcr.git'
def pipelineBranch = binding.hasVariable('PIPELINE_REPO_BRANCH')        ? PIPELINE_REPO_BRANCH        : 'main'
def pipelineCreds  = binding.hasVariable('PIPELINE_REPO_CREDENTIAL_ID') ? PIPELINE_REPO_CREDENTIAL_ID : ''

// Common build-container plumbing shared by every job.
def BUILD_IMAGE = 'netsdk10:latest'
def BUILD_ARGS_BUILD   = '-v /tmp/nuget:/tmp/nuget -e DOTNET_CLI_TELEMETRY_OPTOUT=1 --net=cicd-net -u root -v /var/run/docker.sock:/var/run/docker.sock -v /tmp/trivy-cache:/root/.cache/trivy --group-add 0'
def BUILD_ARGS_PUBLISH = '-v /tmp/nuget:/tmp/nuget -e DOTNET_CLI_TELEMETRY_OPTOUT=1 --net=cicd-net -u root -v /var/run/docker.sock:/var/run/docker.sock --group-add 0'

// Helper: build a pipelineJob that loads `scriptPath` from the pipeline repo.
// `lightweight` true  -> fetch only the Jenkinsfile (cicd-build clones its own source)
//              false  -> full checkout (publish jobs pack the source in this repo)
def makePipeline = { String name, String desc, String scriptPath, boolean lightweight, Closure params ->
    pipelineJob(name) {
        description(desc)
        logRotator {
            numToKeep(30)
            artifactNumToKeep(10)
        }
        parameters(params)
        definition {
            cpsScm {
                scm {
                    git {
                        remote {
                            url(pipelineRepo)
                            if (pipelineCreds) {
                                credentials(pipelineCreds)
                            }
                        }
                        branch(pipelineBranch)
                    }
                }
                scriptPath(scriptPath)
                lightweight(lightweight)
            }
        }
    }
}

// ---------------------------------------------------------------------------
// cicd-build — parameterized, repo-agnostic. The caller passes GIT_URL.
// ---------------------------------------------------------------------------
makePipeline('cicd-build',
    'Build, SBOM, and vulnerability-scan the repository given by GIT_URL. Repo-agnostic: the Jenkinsfile clones GIT_URL@GIT_BRANCH itself.',
    'jenkins/build/Jenkinsfile',
    true) {
    stringParam('GIT_URL', '', 'Git repository URL to build (required). The Jenkinsfile clones this itself.')
    stringParam('GIT_BRANCH', 'main', 'Branch, tag, or ref to build')
    stringParam('GIT_CREDENTIALS_ID', '', 'Jenkins credentials id for cloning a private repo (blank = public/anonymous)')
    stringParam('BUILD_CONTAINER_IMAGE', BUILD_IMAGE, 'Image for the build container')
    stringParam('BUILD_CONTAINER_ARGS', BUILD_ARGS_BUILD, 'Arguments for the build container')
    stringParam('BUILD_FILE', 'src/app/cicd.sln', 'File to build (sln or csproj), relative to the cloned repo root')
    stringParam('BASE_VER', '1.0.0', 'Base version (Major.Minor.Patch) used to derive the build versions')
    stringParam('CYCLONEDX_TOOL_VERSION', '5.4.0', 'Version of the dotnet CycloneDX global tool')
    stringParam('TRIVY_VERSION', 'v0.55.0', 'Trivy release tag used to enrich the SBOM (bom-vex.json)')
    choiceParam('FAIL_ON_SEVERITY', ['none', 'high', 'critical'], 'Fail the build when vulnerabilities at this severity (or worse) are present')
}

// ---------------------------------------------------------------------------
// cicd-publish-nexus-nuget — packs + pushes the .nupkg to Nexus, uploads SBOM.
// ---------------------------------------------------------------------------
makePipeline('cicd-publish-nexus-nuget',
    'Pack the upstream build and push the NuGet package + SBOM to Nexus.',
    'jenkins/publish/nexus/nuget/Jenkinsfile',
    true) {
    stringParam('BUILD_CONTAINER_IMAGE', BUILD_IMAGE, 'Image for the build container')
    stringParam('BUILD_CONTAINER_ARGS', BUILD_ARGS_PUBLISH, 'Arguments for the build container')
    stringParam('NUGET_SOURCE', 'http://nexus:8081/repository/nuget-hosted/', 'NuGet feed URL (Nexus hosted repo)')
    stringParam('NUGET_API_KEY_CREDENTIAL_ID', 'rhythm-nuget', 'Jenkins credential id (Secret Text) holding the NuGet API key')
    stringParam('SBOM_NEXUS_REPO_URL', 'http://nexus:8081/repository/sboms/', 'Nexus raw (hosted) repo URL for bom.json + vulnerabilities.json')
    stringParam('SBOM_NEXUS_CREDENTIAL_ID', 'nexus-sbom', 'Jenkins credential id (Username/Password) for the Nexus REST API')
    stringParam('GIT_CREDENTIALS_ID', '', 'Jenkins credentials id for cloning a private app repo (blank = public/anonymous)')
    stringParam('SOURCE_BUILD_JOB', 'cicd-build', 'Upstream build job whose build-info.json is pulled in')
    stringParam('SOURCE_BUILD_NUMBER', '', 'Specific upstream build number to publish. Blank = last successful build.')
}

// ---------------------------------------------------------------------------
// cicd-publish-nexus-docker — builds + pushes the container image to Nexus.
// ---------------------------------------------------------------------------
makePipeline('cicd-publish-nexus-docker',
    'Build the application container image and push it to the Nexus docker registry.',
    'jenkins/publish/nexus/docker/Jenkinsfile',
    true) {
    stringParam('BUILD_CONTAINER_IMAGE', BUILD_IMAGE, 'Image for the build container')
    stringParam('BUILD_CONTAINER_ARGS', BUILD_ARGS_PUBLISH, 'Arguments for the build container')
    stringParam('DOCKER_BUILD_FILE', '', 'Optional Dockerfile override (path within the app repo). Blank = built-in copy-only runtime Dockerfile generated by the job')
    stringParam('CONTAINER_NAME', '', 'Optional single-container override; normally blank — containers come from the cicd-build manifest in build-info.json')
    stringParam('NEXUS_DOCKER_HOST', 'nexus:8082', 'Nexus docker registry host:port')
    stringParam('NEXUS_DOCKER_CREDENTIAL_ID', 'rhythm-docker', 'Jenkins credential id (Username/Password) for the Nexus docker registry')
    stringParam('NEXUS_DOCKER_USER', 'admin', 'Nexus docker registry username')
    stringParam('NEXUS_DOCKER_PROTOCOL', 'http://', 'Nexus communications protocol (http:// or https://)')
    stringParam('GIT_CREDENTIALS_ID', '', 'Jenkins credentials id for cloning a private app repo (blank = public/anonymous)')
    stringParam('SOURCE_BUILD_JOB', 'cicd-build', 'Upstream build job whose build-info.json is pulled in')
    stringParam('SOURCE_BUILD_NUMBER', '', 'Specific upstream build number to publish. Blank = last successful build.')
}
