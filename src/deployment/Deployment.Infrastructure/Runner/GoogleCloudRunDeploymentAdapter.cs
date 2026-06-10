using Deployment.Application.Abstractions;
using Deployment.Contracts.Environments;
using Deployment.Contracts.Releases;
using Google.Api.Gax;
using Google.Api.Gax.Grpc;
using Google.Cloud.Run.V2;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Deployment.Infrastructure.Runner;

/// <summary>
/// Deploys a container release to Google Cloud Run by updating the target
/// service's revision template to the release's image and rolling 100% of
/// traffic to the new revision.
///
/// The target's <c>ResourceId</c> is the Cloud Run service resource name
/// (<c>projects/{project}/locations/{region}/services/{service}</c>); the
/// release's <c>ArtifactUri</c> is the image reference (deploy by digest).
/// Resolved secret bindings whose URI is a Secret Manager version
/// (<c>projects/*/secrets/*/versions/*</c>) are wired as env vars via
/// <see cref="SecretKeySelector"/>.
///
/// Auth is Application Default Credentials — see <see cref="GoogleCloudRunOptions"/>.
/// The client is built once and reused (the adapter is a singleton).
/// </summary>
internal sealed class GoogleCloudRunDeploymentAdapter : IDeploymentAdapter
{
    private readonly IOptionsMonitor<GoogleCloudRunOptions> _options;
    private readonly IArtifactPromoter _promoter;
    private readonly ILogger<GoogleCloudRunDeploymentAdapter> _logger;
    private readonly SemaphoreSlim _clientGate = new(1, 1);
    private ServicesClient? _client;

    public GoogleCloudRunDeploymentAdapter(
        IOptionsMonitor<GoogleCloudRunOptions> options,
        IArtifactPromoter promoter,
        ILogger<GoogleCloudRunDeploymentAdapter> logger)
    {
        _options = options;
        _promoter = promoter;
        _logger = logger;
    }

    public TargetKindDto TargetKind => TargetKindDto.GoogleCloudRun;

    public async Task<DeploymentExecutionOutcome> ExecuteAsync(
        DeploymentExecutionContext context, CancellationToken cancellationToken = default)
    {
        // --- Validate the inputs the Cloud Run API requires. ---
        if (context.ArtifactType != ArtifactTypeDto.ContainerImage)
            return DeploymentExecutionOutcome.Failure(
                $"Cloud Run requires a ContainerImage release; release {context.ReleaseId} is {context.ArtifactType}.");

        if (string.IsNullOrWhiteSpace(context.ArtifactUri))
            return DeploymentExecutionOutcome.Failure(
                $"Release {context.ReleaseId} has no ArtifactUri (image reference) to deploy.");

        if (!ServiceName.TryParse(context.Target.ResourceId, out var serviceName))
            return DeploymentExecutionOutcome.Failure(
                $"Target ResourceId '{context.Target.ResourceId}' is not a Cloud Run service resource name " +
                "(expected projects/{project}/locations/{region}/services/{service}).");

        var image = context.ArtifactUri.Trim();
        var opts = _options.CurrentValue;

        _logger.LogInformation(
            "[cloudrun] Deploying {Unit} v{Version} → service '{Service}' (project {Project}, region {Region}) image {Image}; {BindingCount} secret binding(s).",
            context.DeployableUnitName, context.ReleaseSemanticVersion, serviceName.ServiceId,
            serviceName.ProjectId, serviceName.LocationId, image, context.SecretBindings.Count);

        var client = await GetClientAsync(cancellationToken).ConfigureAwait(false);

        // --- Read the current service so we mutate-in-place rather than clobber config.
        //     When it doesn't exist, optionally create it (CreateServiceIfMissing). ---
        Service? service = null;
        try
        {
            service = await client.GetServiceAsync(serviceName, CallSettings.FromCancellationToken(cancellationToken))
                .ConfigureAwait(false);
        }
        catch (RpcException rpc) when (rpc.StatusCode == StatusCode.NotFound)
        {
            if (!opts.CreateServiceIfMissing)
                return DeploymentExecutionOutcome.Failure(
                    $"Cloud Run service '{serviceName}' does not exist. Create it first " +
                    "(e.g. scripts/Bootstrap-CloudRunService.ps1) or set " +
                    "Deployment:GoogleCloudRun:CreateServiceIfMissing=true to auto-create a bare service.");
            // Leave service null → created below from a blank template.
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return DeploymentExecutionOutcome.Failure(
                $"Could not read Cloud Run service '{serviceName}': {ex.GetType().Name}: {ex.Message}");
        }

        var creating = service is null;
        service ??= new Service();

        // --- Decision #6: optionally promote the (Nexus) image into GAR, then deploy
        //     the GAR ref. When promotion is off, the release's ref is deployed as-is. ---
        var deployImage = image;
        if (opts.PromoteFromNexus && !string.IsNullOrWhiteSpace(opts.ArtifactRegistryRepository))
        {
            if (!TryBuildGarReference(image, serviceName, opts.ArtifactRegistryRepository, out var garRef, out var refError))
                return DeploymentExecutionOutcome.Failure($"Cannot promote image '{image}' to GAR: {refError}");

            try
            {
                await _promoter.EnsureCopiedAsync(image, garRef, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return DeploymentExecutionOutcome.Failure(
                    $"GAR promotion of '{image}' failed: {ex.GetType().Name}: {ex.Message}");
            }

            _logger.LogInformation("[cloudrun] Promoted {Source} → {Dest} before deploy.", image, garRef);
            deployImage = garRef;
        }

        ApplyImage(service, deployImage);
        ApplySecretEnv(service, context.SecretBindings);
        RouteAllTrafficToLatest(service);

        // --- Update + wait for the new revision to become Ready (LRO). ---
        var readiness = TimeSpan.FromSeconds(Math.Max(1, opts.ReadinessTimeoutSeconds));
        var pollEvery = TimeSpan.FromSeconds(Math.Max(1, opts.ReadinessPollSeconds));
        var pollSettings = new PollSettings(Expiration.FromTimeout(readiness), pollEvery);

        try
        {
            // Create (parent + serviceId) when the service was absent, else update in place.
            var operation = creating
                ? await client.CreateServiceAsync(
                        $"projects/{serviceName.ProjectId}/locations/{serviceName.LocationId}",
                        service, serviceName.ServiceId,
                        CallSettings.FromCancellationToken(cancellationToken))
                    .ConfigureAwait(false)
                : await client.UpdateServiceAsync(
                        service, CallSettings.FromCancellationToken(cancellationToken))
                    .ConfigureAwait(false);

            var completed = await operation
                .PollUntilCompletedAsync(callSettings: CallSettings.FromCancellationToken(cancellationToken),
                                         pollSettings: pollSettings)
                .ConfigureAwait(false);

            var result = completed.Result;
            _logger.LogInformation(
                "[cloudrun] Service '{Service}' {Action}. Ready revision: {Revision}.",
                serviceName.ServiceId, creating ? "created" : "updated", result.LatestReadyRevision);

            return DeploymentExecutionOutcome.Success;
        }
        catch (OperationCanceledException)
        {
            throw; // Let the runner classify cancellation/timeout.
        }
        catch (TimeoutException)
        {
            return DeploymentExecutionOutcome.Failure(
                $"Cloud Run revision for '{serviceName}' did not become Ready within {readiness.TotalSeconds:0}s.");
        }
        catch (Exception ex)
        {
            return DeploymentExecutionOutcome.Failure(
                $"Cloud Run deployment of '{serviceName}' failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ApplyImage(Service service, string image)
    {
        // Cloud Run v2 services always carry a template with at least one
        // container; guard anyway so a malformed service yields a clean error
        // path rather than an NRE.
        if (service.Template is null)
            service.Template = new RevisionTemplate();
        if (service.Template.Containers.Count == 0)
            service.Template.Containers.Add(new Container());

        // Single-container services are the norm here; set the first (ingress)
        // container's image. Clearing the revision name lets Cloud Run mint a
        // fresh revision id for the new image.
        service.Template.Containers[0].Image = image;
        service.Template.Revision = string.Empty;
    }

    /// <summary>
    /// Builds the GAR pull-by-digest reference for a source image. The source must be
    /// a digest ref (<c>host/path@sha256:…</c>); project + region come from the target
    /// Cloud Run service. Result: <c>{region}-docker.pkg.dev/{project}/{repo}/{path}@{digest}</c>.
    /// </summary>
    private static bool TryBuildGarReference(
        string sourceRef, ServiceName service, string repository, out string garRef, out string error)
    {
        garRef = string.Empty;
        error = string.Empty;

        var at = sourceRef.IndexOf('@');
        if (at <= 0 || at == sourceRef.Length - 1)
        {
            error = "source must be a digest reference (host/path@sha256:…)";
            return false;
        }
        var digest = sourceRef[(at + 1)..];
        var hostAndPath = sourceRef[..at];

        var slash = hostAndPath.IndexOf('/');
        if (slash <= 0 || slash == hostAndPath.Length - 1)
        {
            error = "source reference has no image path after the host";
            return false;
        }
        var imagePath = hostAndPath[(slash + 1)..];

        garRef = $"{service.LocationId}-docker.pkg.dev/{service.ProjectId}/{repository.Trim('/')}/{imagePath}@{digest}";
        return true;
    }

    private void ApplySecretEnv(Service service, IReadOnlyList<ResolvedSecretBinding> bindings)
    {
        if (bindings.Count == 0) return;

        var container = service.Template.Containers[0];
        foreach (var binding in bindings)
        {
            if (!TryParseSecretVersion(binding.VersionedSecretUri, out var secret, out var version))
            {
                _logger.LogWarning(
                    "[cloudrun] Secret binding '{Key}' URI '{Uri}' is not a Secret Manager version " +
                    "(projects/*/secrets/*/versions/*); skipping.", binding.Key, binding.VersionedSecretUri);
                continue;
            }

            // Replace any existing env var of the same name so re-deploys are idempotent.
            var existing = container.Env.FirstOrDefault(e => e.Name == binding.Key);
            if (existing is not null) container.Env.Remove(existing);

            container.Env.Add(new EnvVar
            {
                Name = binding.Key,
                ValueSource = new EnvVarSource
                {
                    SecretKeyRef = new SecretKeySelector { Secret = secret, Version = version },
                },
            });
        }
    }

    private static void RouteAllTrafficToLatest(Service service)
    {
        // Default to shipping the new revision: 100% to latest. A future
        // canary strategy would split traffic here based on context.Strategy.
        service.Traffic.Clear();
        service.Traffic.Add(new TrafficTarget
        {
            Type = TrafficTargetAllocationType.Latest,
            Percent = 100,
        });
    }

    /// <summary>
    /// Parses <c>projects/{p}/secrets/{s}/versions/{v}</c> into the
    /// <c>projects/{p}/secrets/{s}</c> secret resource and the version string.
    /// </summary>
    private static bool TryParseSecretVersion(string? uri, out string secret, out string version)
    {
        secret = string.Empty;
        version = string.Empty;
        if (string.IsNullOrWhiteSpace(uri)) return false;

        var parts = uri.Trim().Split('/');
        // projects p secrets s versions v  → 6 segments
        if (parts.Length != 6 ||
            parts[0] != "projects" || parts[2] != "secrets" || parts[4] != "versions")
            return false;

        secret = $"projects/{parts[1]}/secrets/{parts[3]}";
        version = parts[5];
        return true;
    }

    private async Task<ServicesClient> GetClientAsync(CancellationToken ct)
    {
        if (_client is not null) return _client;

        await _clientGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_client is not null) return _client;

            // Auth is always ambient ADC: GOOGLE_APPLICATION_CREDENTIALS, gcloud
            // user creds, or Workload Identity / metadata server. Keeping key
            // material out of app config is deliberate (project secret rules).
            _client = await new ServicesClientBuilder().BuildAsync(ct).ConfigureAwait(false);
            return _client;
        }
        finally
        {
            _clientGate.Release();
        }
    }
}
