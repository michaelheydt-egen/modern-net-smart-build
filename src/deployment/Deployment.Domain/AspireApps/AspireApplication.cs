using Deployment.Domain.Common;
using Deployment.Domain.AspireApps.Events;

namespace Deployment.Domain.AspireApps;

/// <summary>
/// A registered .NET Aspire application deployed as a whole to Kubernetes via Aspir8. CI produces the
/// Kustomize output (<c>aspirate generate</c>) and publishes it as an artifact; deploying fetches that
/// <see cref="ManifestSource"/>, repoints/digest-pins the images, and runs <c>aspirate apply</c> against
/// the target <see cref="EnvironmentId"/>'s Kubernetes context/namespace. Distinct from the per-service
/// Cloud Run model.
/// </summary>
public sealed class AspireApplication : AggregateRoot<Guid>
{
    public string Name { get; private set; }
    public string? Description { get; private set; }

    /// <summary>The target <see cref="Environments.DeploymentEnvironment"/> (provides the kube context + namespace).</summary>
    public Guid EnvironmentId { get; private set; }

    /// <summary>URL of the CI-produced Kustomize-output archive (.zip/.tar.gz) of <c>aspirate generate</c>.</summary>
    public string ManifestSource { get; private set; }

    /// <summary>Optional version/build label for the deploy (informational; the images carry their own tags/digests).</summary>
    public string? Version { get; private set; }

    /// <summary>
    /// Explicit CI identity this app tracks — the <c>AspireAppPublished.AppName</c> emitted by the build
    /// (derived from the <c>*.AppHost</c> project). When set, the handoff matches on this instead of
    /// <see cref="Name"/>, so the deployment app can be renamed freely. When null, name-matching applies
    /// (backward compatible).
    /// </summary>
    public string? SourceKey { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>When true, a CI <c>AspireAppPublished</c> event matching this app auto-triggers a deployment.</summary>
    public bool AutoDeploy { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private AspireApplication()
    {
        Name = string.Empty;
        ManifestSource = string.Empty;
    }

    public AspireApplication(Guid id, string name, string? description, Guid environmentId, string manifestSource, string? version, string? sourceKey, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id cannot be empty.", nameof(id));
        if (environmentId == Guid.Empty) throw new ArgumentException("EnvironmentId cannot be empty.", nameof(environmentId));
        Id = id;
        Name = Require(name, nameof(name));
        Description = Clean(description);
        EnvironmentId = environmentId;
        ManifestSource = Require(manifestSource, nameof(manifestSource));
        Version = Clean(version);
        SourceKey = Clean(sourceKey);
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        RaiseEvent(new AspireApplicationRegistered(Id, Name, createdAtUtc));
    }

    public void Update(string name, string? description, Guid environmentId, string manifestSource, string? version, string? sourceKey, DateTimeOffset occurredAtUtc)
    {
        if (environmentId == Guid.Empty) throw new ArgumentException("EnvironmentId cannot be empty.", nameof(environmentId));
        Name = Require(name, nameof(name));
        Description = Clean(description);
        EnvironmentId = environmentId;
        ManifestSource = Require(manifestSource, nameof(manifestSource));
        Version = Clean(version);
        SourceKey = Clean(sourceKey);
        UpdatedAtUtc = occurredAtUtc;
        RaiseEvent(new AspireApplicationUpdated(Id, Name, occurredAtUtc));
    }

    public void ChangeActivation(bool active, DateTimeOffset occurredAtUtc)
    {
        if (IsActive == active) return;
        IsActive = active;
        UpdatedAtUtc = occurredAtUtc;
    }

    public void SetAutoDeploy(bool autoDeploy, DateTimeOffset occurredAtUtc)
    {
        if (AutoDeploy == autoDeploy) return;
        AutoDeploy = autoDeploy;
        UpdatedAtUtc = occurredAtUtc;
        RaiseEvent(new AspireApplicationAutoDeployChanged(Id, autoDeploy, occurredAtUtc));
    }

    /// <summary>
    /// Refresh the manifest source (+ optional version) from a CI publish. Idempotent: a repeat with the
    /// same source and version is a no-op (returns false), so the consumer can skip a redundant auto-deploy.
    /// </summary>
    public bool ApplyPublishedManifest(string manifestSource, string? version, DateTimeOffset occurredAtUtc)
    {
        var source = Require(manifestSource, nameof(manifestSource));
        var ver = Clean(version);
        if (string.Equals(ManifestSource, source, StringComparison.Ordinal) &&
            string.Equals(Version, ver, StringComparison.Ordinal))
            return false;

        ManifestSource = source;
        Version = ver;
        UpdatedAtUtc = occurredAtUtc;
        RaiseEvent(new AspireApplicationManifestPublished(Id, Name, source, ver, occurredAtUtc));
        return true;
    }

    private static string Require(string value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} cannot be empty.", name) : value.Trim();
    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
