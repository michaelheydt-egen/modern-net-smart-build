namespace Cicd.Web.Admin.Services.Gcp;

public interface IGcpClient
{
    /// <summary>True if SDK clients constructed at startup; false means ADC was missing/invalid.</summary>
    bool IsConfigured { get; }

    /// <summary>If <see cref="IsConfigured"/> is false, the error explaining why.</summary>
    string? ConfigurationError { get; }

    Task<IReadOnlyList<GarImage>> ListArtifactsAsync(GcpEnvironment env, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CloudRunService>> ListCloudRunServicesAsync(GcpEnvironment env, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a single GAR version. All tags pointing at the underlying digest are removed.
    /// Waits for the long-running delete operation to complete before returning.
    /// </summary>
    Task DeleteArtifactAsync(GcpEnvironment env, GarImage image, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a single Cloud Run service (all revisions, all traffic). Waits for the
    /// long-running delete operation to complete before returning.
    /// </summary>
    Task DeleteCloudRunServiceAsync(GcpEnvironment env, CloudRunService service, CancellationToken cancellationToken = default);
}
