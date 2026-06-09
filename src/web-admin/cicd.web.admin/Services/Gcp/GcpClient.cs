using Google.Cloud.ArtifactRegistry.V1;
using Google.Cloud.Run.V2;

// Cloud Run SDK defines a `Task` type (background-task resource); disambiguate
// from System.Threading.Tasks.Task used in our async method signatures.
using Task = System.Threading.Tasks.Task;

namespace Cicd.Web.Admin.Services.Gcp;

public sealed class GcpClient : IGcpClient
{
    private readonly ArtifactRegistryClient? _gar;
    private readonly ServicesClient? _run;
    private readonly ILogger<GcpClient> _logger;

    public bool IsConfigured => _gar is not null && _run is not null;
    public string? ConfigurationError { get; }

    public GcpClient(ILogger<GcpClient> logger)
    {
        _logger = logger;

        // ADC (Application Default Credentials) is resolved via the standard chain:
        // GOOGLE_APPLICATION_CREDENTIALS env var → gcloud user creds → metadata server.
        // If none are present, the SDK clients throw at construction time; we record the
        // error rather than failing app startup so the UI can surface it gracefully.
        try
        {
            _gar = ArtifactRegistryClient.Create();
            _run = ServicesClient.Create();
        }
        catch (Exception ex)
        {
            ConfigurationError = ex.Message;
            _logger.LogWarning(
                "GCP clients could not be initialized: {Message}. " +
                "Set GOOGLE_APPLICATION_CREDENTIALS to a valid service-account key file " +
                "if you intend to use the Google page.",
                ex.Message);
        }
    }

    public async Task<IReadOnlyList<GarImage>> ListArtifactsAsync(GcpEnvironment env, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var parent = RepositoryName.FromProjectLocationRepository(env.ProjectId, env.Region, env.ArtifactRegistry);
        var request = new ListDockerImagesRequest { Parent = parent.ToString() };

        var results = new List<GarImage>();
        await foreach (var img in _gar!.ListDockerImagesAsync(request).WithCancellation(cancellationToken))
        {
            // img.Name is the full resource path; we want just the image's logical name (last segment
            // before the @sha256 digest, with any URL-encoded slashes preserved).
            // img.Uri is "us-west1-docker.pkg.dev/<project>/<repo>/<name>@sha256:..." — much easier.
            var imageName = ExtractImageName(img.Uri);

            // Sort tags: floating-style (v1, latest) first, then ci-N, then git-sha — by length-asc
            // is a rough proxy that works well enough without parsing.
            var tags = img.Tags?.OrderBy(t => t.Length).ThenBy(t => t).ToArray() ?? Array.Empty<string>();

            results.Add(new GarImage(
                Name:      imageName,
                Tags:      tags,
                UpdatedAt: img.UpdateTime?.ToDateTimeOffset() ?? DateTimeOffset.MinValue,
                SizeBytes: img.ImageSizeBytes > 0 ? img.ImageSizeBytes : null,
                Digest:    ExtractDigest(img.Uri),
                FullUri:   img.Uri));
        }

        return results.OrderBy(i => i.Name).ThenByDescending(i => i.UpdatedAt).ToArray();
    }

    public async Task<IReadOnlyList<CloudRunService>> ListCloudRunServicesAsync(GcpEnvironment env, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var parent = $"projects/{env.ProjectId}/locations/{env.Region}";
        var request = new ListServicesRequest { Parent = parent };

        var results = new List<CloudRunService>();
        await foreach (var svc in _run!.ListServicesAsync(request).WithCancellation(cancellationToken))
        {
            // Service name is "projects/.../services/<name>" — take the last segment.
            var name = svc.Name.Split('/').LastOrDefault() ?? svc.Name;

            // Cloud Run V2 surfaces readiness via TerminalCondition. The enum values are
            // versioned ("ConditionSucceeded", "ConditionFailed", "ConditionPending", etc.);
            // strip the "Condition" prefix for a clean display label.
            var status = svc.TerminalCondition?.State.ToString() ?? "Unknown";
            if (status.StartsWith("Condition", StringComparison.Ordinal))
            {
                status = status.Substring("Condition".Length);
            }
            // Normalize the most useful state name for downstream UI mapping.
            if (status == "Succeeded") status = "Ready";

            var image = svc.Template?.Containers?.FirstOrDefault()?.Image ?? string.Empty;
            var url = svc.Uri ?? string.Empty;
            var latestRev = svc.LatestReadyRevision is { Length: > 0 } rev
                ? rev.Split('/').LastOrDefault() ?? rev
                : string.Empty;

            results.Add(new CloudRunService(
                Name:           name,
                Url:            url,
                LatestRevision: latestRev,
                Image:          image,
                Status:         status,
                UpdatedAt:      svc.UpdateTime?.ToDateTimeOffset() ?? DateTimeOffset.MinValue));
        }

        return results.OrderBy(s => s.Name).ToArray();
    }

    public async Task DeleteArtifactAsync(GcpEnvironment env, GarImage image, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        // GAR's delete API operates on the underlying Version (one per digest), not the
        // DockerImage view. Deleting a version removes all tags pointing at it.
        var versionName = VersionName.FromProjectLocationRepositoryPackageVersion(
            env.ProjectId, env.Region, env.ArtifactRegistry,
            image.Name,     // package id == image name (single-segment)
            image.Digest);  // version id == "sha256:..."
        var request = new DeleteVersionRequest
        {
            Name  = versionName.ToString(),
            // GAR refuses to delete a version that has tags pointing at it unless force=true.
            // Every image in our list has tags, so without this the API returns
            // FAILED_PRECONDITION on the long-running operation.
            Force = true,
        };

        var op = await _gar!.DeleteVersionAsync(request, cancellationToken: cancellationToken);
        var completed = await op.PollUntilCompletedAsync();
        ThrowIfFaulted(completed, $"delete GAR version {image.Name}@{image.Digest}");
    }

    public async Task DeleteCloudRunServiceAsync(GcpEnvironment env, CloudRunService service, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        // Both GAR and Cloud Run SDKs define a `ServiceName` type — qualify the Cloud Run one.
        var serviceName = Google.Cloud.Run.V2.ServiceName.FromProjectLocationService(
            env.ProjectId, env.Region, service.Name);

        var op = await _run!.DeleteServiceAsync(serviceName, cancellationToken: cancellationToken);
        var completed = await op.PollUntilCompletedAsync();
        ThrowIfFaulted(completed, $"delete Cloud Run service {service.Name}");
    }

    /// <summary>
    /// GCP long-running operations don't throw on failure — they complete normally with
    /// <c>IsFaulted == true</c> and the error in <c>Exception</c>. Surface that as an
    /// exception so the caller's snackbar/log reflects the actual failure.
    /// </summary>
    private static void ThrowIfFaulted<TResource, TMetadata>(
        Google.LongRunning.Operation<TResource, TMetadata> completed,
        string what)
        where TResource : class, Google.Protobuf.IMessage<TResource>, new()
        where TMetadata : class, Google.Protobuf.IMessage<TMetadata>, new()
    {
        if (completed.IsFaulted)
        {
            var rpc = completed.Exception;
            throw new InvalidOperationException(
                $"Failed to {what}: {rpc?.Message ?? "operation completed with error status."}");
        }
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                $"GCP credentials are not configured. {ConfigurationError ?? "(unknown cause)"}");
        }
    }

    private static string ExtractImageName(string uri)
    {
        // uri: "us-west1-docker.pkg.dev/<project>/<repo>/<image-name>@sha256:..."
        var at = uri.IndexOf('@');
        var path = at >= 0 ? uri[..at] : uri;
        var lastSlash = path.LastIndexOf('/');
        return lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
    }

    private static string ExtractDigest(string uri)
    {
        var at = uri.IndexOf('@');
        return at >= 0 ? uri[(at + 1)..] : string.Empty;
    }
}
