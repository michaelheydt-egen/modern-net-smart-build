namespace Cicd.Web.Admin.Services.Nexus;

public interface INexusClient
{
    /// <summary>True if Url + Password were supplied at startup.</summary>
    bool IsConfigured { get; }

    /// <summary>If <see cref="IsConfigured"/> is false, the reason (missing field).</summary>
    string? ConfigurationError { get; }

    /// <summary>Repository name being enumerated (echoes <see cref="NexusOptions.NuGetHostedRepository"/>).</summary>
    string NuGetRepositoryName { get; }

    /// <summary>Repository name being enumerated (echoes <see cref="NexusOptions.DockerHostedRepository"/>).</summary>
    string DockerRepositoryName { get; }

    /// <summary>
    /// Returns every package version in the configured nuget-hosted repository.
    /// Walks Nexus's continuation-token paging until exhausted.
    /// </summary>
    Task<IReadOnlyList<NuGetPackage>> ListNuGetPackagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every image+tag in the configured docker-hosted repository.
    /// Walks Nexus's continuation-token paging until exhausted.
    /// </summary>
    Task<IReadOnlyList<DockerImage>> ListDockerImagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a single Nexus component (one package version, all its assets).
    /// </summary>
    Task DeleteNuGetPackageAsync(NuGetPackage package, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a single Nexus component (one image+tag, all its associated assets).
    /// Shared blob layers used by other tags are retained by Nexus.
    /// </summary>
    Task DeleteDockerImageAsync(DockerImage image, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every asset (manifests + blobs) in the configured docker-hosted repository.
    /// Used by the "wipe orphans after component delete" pass — Nexus's components API
    /// only manipulates the tag/component layer; assets linger underneath.
    /// </summary>
    Task<IReadOnlyList<NexusAsset>> ListDockerAssetsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a single asset by id. Removes the asset record from Nexus but does NOT
    /// free the on-disk blob bytes — only the compact-blob-store task does that.
    /// </summary>
    Task DeleteAssetAsync(NexusAsset asset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds every Nexus "Compact blob store" task and triggers each to run async.
    /// Returns the ids of tasks that were successfully POSTed. Returns an empty list
    /// if the caller lacks the <c>nx-tasks-*</c> privilege (Nexus returns 403) or no
    /// such tasks are configured — the caller can surface this as "skipped".
    /// </summary>
    Task<IReadOnlyList<string>> TriggerCompactBlobStoreTasksAsync(CancellationToken cancellationToken = default);
}
