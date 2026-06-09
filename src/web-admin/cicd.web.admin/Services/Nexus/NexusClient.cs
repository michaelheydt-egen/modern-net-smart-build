using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cicd.Web.Admin.Services.Nexus;

public sealed class NexusClient : INexusClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient? _http;
    private readonly NexusOptions _options;
    private readonly ILogger<NexusClient> _logger;

    public bool IsConfigured => _http is not null;
    public string? ConfigurationError { get; }
    public string NuGetRepositoryName  => _options.NuGetHostedRepository;
    public string DockerRepositoryName => _options.DockerHostedRepository;

    public NexusClient(NexusOptions options, ILogger<NexusClient> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger  = logger;

        // Validate up front so the UI can render a useful banner instead of failing
        // on every list call. Missing Url is fatal; missing Password is fatal because
        // Nexus typically requires authentication to browse hosted repos.
        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            ConfigurationError = "Nexus:Url is not set.";
            return;
        }
        if (string.IsNullOrWhiteSpace(_options.Password))
        {
            ConfigurationError = "Nexus:Password is not set (env: Nexus__Password).";
            return;
        }

        _http = new HttpClient { BaseAddress = new Uri(_options.Url.TrimEnd('/') + "/") };
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.User}:{_options.Password}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<NuGetPackage>> ListNuGetPackagesAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var items = await ListAllComponentsAsync(_options.NuGetHostedRepository, ToNuGetModel, cancellationToken);
        // Stable order: package name asc, then version desc (newest first).
        return items
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(p => p.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<DockerImage>> ListDockerImagesAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var items = await ListAllComponentsAsync(_options.DockerHostedRepository, ToDockerModel, cancellationToken);
        // Stable order: image name asc, then tag asc.
        return items
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task DeleteNuGetPackageAsync(NuGetPackage package, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        return DeleteComponentAsync(package.ComponentId, $"{package.Name} {package.Version}", cancellationToken);
    }

    public Task DeleteDockerImageAsync(DockerImage image, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        return DeleteComponentAsync(image.ComponentId, $"{image.Name}:{image.Tag}", cancellationToken);
    }

    public async Task<IReadOnlyList<NexusAsset>> ListDockerAssetsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var items = await ListAllAssetsAsync(_options.DockerHostedRepository, ToNexusAsset, cancellationToken);
        // Delete order: manifests first, then blobs. Manifests reference blobs, so
        // removing manifests first reduces orphan-reference noise Nexus has to track.
        return items
            .OrderByDescending(a => a.Path.Contains("/manifests/", StringComparison.OrdinalIgnoreCase))
            .ThenBy(a => a.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task DeleteAssetAsync(NexusAsset asset, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        return DeleteAssetByIdAsync(asset.Id, asset.Path, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> TriggerCompactBlobStoreTasksAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        TaskListDto? tasks;
        try
        {
            tasks = await _http!.GetFromJsonAsync<TaskListDto>("service/rest/v1/tasks", JsonOpts, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Forbidden
                                                              or System.Net.HttpStatusCode.Unauthorized)
        {
            // Admin perm missing — surface as "skipped" to the caller rather than throwing.
            return Array.Empty<string>();
        }
        if (tasks?.Items is null) return Array.Empty<string>();

        var triggered = new List<string>();
        foreach (var t in tasks.Items)
        {
            // There may be multiple compact tasks (one per configured blob store);
            // trigger all of them so disk is freed wherever the docker repo lives.
            if (!string.Equals(t.Type, "blobstore.compact", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(t.Id)) continue;

            using var resp = await _http!.PostAsync(
                $"service/rest/v1/tasks/{Uri.EscapeDataString(t.Id)}/run",
                content: null,
                cancellationToken);
            if (resp.IsSuccessStatusCode)
            {
                triggered.Add(t.Id);
            }
        }
        return triggered;
    }

    private async Task<List<T>> ListAllAssetsAsync<T>(
        string repository,
        Func<AssetDto, T> map,
        CancellationToken cancellationToken)
    {
        var results = new List<T>();
        string? token = null;
        do
        {
            var path = $"service/rest/v1/assets?repository={Uri.EscapeDataString(repository)}";
            if (token is { Length: > 0 })
            {
                path += $"&continuationToken={Uri.EscapeDataString(token)}";
            }

            var page = await _http!.GetFromJsonAsync<AssetListDto>(path, JsonOpts, cancellationToken)
                ?? throw new InvalidOperationException("Nexus returned empty body for assets listing.");

            if (page.Items is { Length: > 0 })
            {
                foreach (var a in page.Items) results.Add(map(a));
            }
            token = page.ContinuationToken;
        }
        while (!string.IsNullOrEmpty(token));

        return results;
    }

    private async Task DeleteAssetByIdAsync(string assetId, string displayLabel, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(assetId))
        {
            throw new InvalidOperationException(
                $"Cannot delete asset '{displayLabel}': id was not populated by the listing call.");
        }

        using var resp = await _http!.DeleteAsync(
            $"service/rest/v1/assets/{Uri.EscapeDataString(assetId)}",
            cancellationToken);

        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return;
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Nexus delete of asset {displayLabel} failed: {(int)resp.StatusCode} {resp.StatusCode}. {body}".Trim(),
                inner: null,
                statusCode: resp.StatusCode);
        }
    }

    private static NexusAsset ToNexusAsset(AssetDto a) => new(
        Id:           a.Id   ?? string.Empty,
        Path:         a.Path ?? string.Empty,
        FileSize:     a.FileSize,
        LastModified: a.LastModified);

    /// <summary>
    /// Walks Nexus's continuation-token paging for a single repository and projects
    /// each component into <typeparamref name="T"/>. Shared by the NuGet + Docker list calls.
    /// </summary>
    private async Task<List<T>> ListAllComponentsAsync<T>(
        string repository,
        Func<ComponentDto, T> map,
        CancellationToken cancellationToken)
    {
        var results = new List<T>();
        string? token = null;
        do
        {
            var path = $"service/rest/v1/components?repository={Uri.EscapeDataString(repository)}";
            if (token is { Length: > 0 })
            {
                path += $"&continuationToken={Uri.EscapeDataString(token)}";
            }

            var page = await _http!.GetFromJsonAsync<ComponentListDto>(path, JsonOpts, cancellationToken)
                ?? throw new InvalidOperationException("Nexus returned empty body for components listing.");

            if (page.Items is { Length: > 0 })
            {
                foreach (var c in page.Items) results.Add(map(c));
            }
            token = page.ContinuationToken;
        }
        while (!string.IsNullOrEmpty(token));

        return results;
    }

    /// <summary>
    /// Issues <c>DELETE /service/rest/v1/components/{id}</c>. 204 = success; 404 is
    /// treated as success (component already gone — caller's intent satisfied); other
    /// non-2xx responses throw with the response body for diagnosis.
    /// </summary>
    private async Task DeleteComponentAsync(string componentId, string displayLabel, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(componentId))
        {
            throw new InvalidOperationException(
                $"Cannot delete '{displayLabel}': component id was not populated by the listing call.");
        }

        using var resp = await _http!.DeleteAsync(
            $"service/rest/v1/components/{Uri.EscapeDataString(componentId)}",
            cancellationToken);

        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return;
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Nexus delete of {displayLabel} failed: {(int)resp.StatusCode} {resp.StatusCode}. {body}".Trim(),
                inner: null,
                statusCode: resp.StatusCode);
        }
    }

    private static NuGetPackage ToNuGetModel(ComponentDto c)
    {
        // Per package version Nexus typically stores at least the .nupkg asset; for
        // size + uploaded-at we prefer that one. Fall back to the first asset present.
        var nupkg = c.Assets?.FirstOrDefault(a => a.Path?.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) == true);
        var asset = nupkg ?? c.Assets?.FirstOrDefault();

        return new NuGetPackage(
            ComponentId: c.Id      ?? string.Empty,
            Name:        c.Name    ?? string.Empty,
            Version:     c.Version ?? string.Empty,
            SizeBytes:   asset?.FileSize,
            UploadedAt:  asset?.LastModified,
            DownloadUrl: asset?.DownloadUrl,
            Sha256:      asset?.Checksum?.Sha256);
    }

    private static DockerImage ToDockerModel(ComponentDto c)
    {
        // For a Docker tag the most reliable "pushed-at" timestamp is the manifest's
        // lastModified — config + layer blobs are often shared across tags and have
        // older dates. Identify the manifest by content-type (Docker / OCI variants
        // all contain "manifest"); fall back to the first asset if none matches.
        var manifest = c.Assets?.FirstOrDefault(a =>
            a.ContentType?.Contains("manifest", StringComparison.OrdinalIgnoreCase) == true);
        var primary = manifest ?? c.Assets?.FirstOrDefault();

        // Sum of asset sizes. For most Nexus Docker hosted repos this is just the
        // manifest (layers are separate components shared across tags), so the
        // number under-represents the on-disk image size. Better than nothing.
        long? totalSize = c.Assets is { Length: > 0 } assets
            ? assets.Sum(a => a.FileSize ?? 0L)
            : null;
        if (totalSize is 0) totalSize = null;

        return new DockerImage(
            ComponentId: c.Id      ?? string.Empty,
            Name:        c.Name    ?? string.Empty,
            Tag:         c.Version ?? string.Empty,
            SizeBytes:   totalSize,
            UploadedAt:  primary?.LastModified);
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                $"Nexus is not configured. {ConfigurationError ?? "(unknown cause)"}");
        }
    }

    public void Dispose() => _http?.Dispose();

    // --- DTOs (Nexus REST v1) ---

    private sealed record ComponentListDto(ComponentDto[]? Items, string? ContinuationToken);

    private sealed record ComponentDto(
        string? Id,
        string? Repository,
        string? Format,
        string? Group,
        string? Name,
        string? Version,
        AssetDto[]? Assets);

    private sealed record AssetDto(
        string? DownloadUrl,
        string? Path,
        string? Id,
        string? Repository,
        string? Format,
        ChecksumDto? Checksum,
        string? ContentType,
        DateTimeOffset? LastModified,
        long? FileSize);

    private sealed record ChecksumDto(string? Sha1, string? Md5, string? Sha256, string? Sha512);

    private sealed record AssetListDto(AssetDto[]? Items, string? ContinuationToken);

    private sealed record TaskListDto(TaskDto[]? Items);

    private sealed record TaskDto(
        string? Id,
        string? Name,
        string? Type,
        string? CurrentState,
        string? LastRunResult);
}
