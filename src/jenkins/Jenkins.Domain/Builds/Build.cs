using Jenkins.Domain.Builds.Events;
using Jenkins.Domain.Common;

namespace Jenkins.Domain.Builds;

/// <summary>
/// Aggregate root for one CI run of one commit — the unit of truth (handoff §2).
/// Carries the source revision, the resolved versions, status/timing, and the
/// supply-chain quality outputs; owns the <see cref="BuildArtifact"/> rows it
/// produced. Natural key is (<see cref="CiJobName"/>, <see cref="CiBuildNumber"/>),
/// which the ingestion layer upserts against as it syncs from Jenkins.
///
/// Lifecycle: <see cref="BuildStatus.Running"/> on creation, then exactly one
/// terminal transition. Versions and quality may arrive before or at completion
/// (they're produced mid-build), so they're recorded via their own methods.
/// </summary>
public sealed class Build : AggregateRoot<Guid>
{
    public Guid RepositoryId { get; private set; }
    public string CiJobName { get; private set; }
    public int CiBuildNumber { get; private set; }

    /// <summary>Clickable Jenkins run URL (<c>http://jenkins:8080/job/cicd-build/42/</c>).</summary>
    public string CiRunUrl { get; private set; }

    /// <summary>Programmatic CI run id (<c>cicd-build/#42</c>) — feeds Release provenance.</summary>
    public string CiRunId { get; private set; }

    public SourceRevision SourceRevision { get; private set; }
    public BuildVersions? Versions { get; private set; }
    public BuildQuality? Quality { get; private set; }

    public BuildStatus Status { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public long? DurationMs { get; private set; }
    public string? TriggeredBy { get; private set; }

    /// <summary>
    /// For Aspire builds, the Nexus URL of the published Kustomize-manifest archive. Set once
    /// via <see cref="RecordAspireManifest"/>; doubles as the idempotency guard so the sync
    /// worker doesn't re-emit the handoff each tick.
    /// </summary>
    public string? AspireManifestUrl { get; private set; }

    private readonly List<BuildArtifact> _artifacts = new();
    public IReadOnlyCollection<BuildArtifact> Artifacts => _artifacts.AsReadOnly();

    public bool IsTerminal =>
        Status is BuildStatus.Succeeded or BuildStatus.Failed or BuildStatus.Aborted;

    private Build()
    {
        CiJobName = string.Empty;
        CiRunUrl = string.Empty;
        CiRunId = string.Empty;
        SourceRevision = null!;
    }

    public Build(
        Guid id,
        Guid repositoryId,
        string ciJobName,
        int ciBuildNumber,
        string ciRunUrl,
        string ciRunId,
        SourceRevision sourceRevision,
        string? triggeredBy,
        DateTimeOffset startedAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id cannot be empty.", nameof(id));
        if (repositoryId == Guid.Empty)
            throw new ArgumentException("RepositoryId cannot be empty.", nameof(repositoryId));
        if (string.IsNullOrWhiteSpace(ciJobName))
            throw new ArgumentException("CiJobName cannot be empty.", nameof(ciJobName));
        if (ciBuildNumber <= 0)
            throw new ArgumentException("CiBuildNumber must be positive.", nameof(ciBuildNumber));
        if (string.IsNullOrWhiteSpace(ciRunUrl))
            throw new ArgumentException("CiRunUrl cannot be empty.", nameof(ciRunUrl));
        if (string.IsNullOrWhiteSpace(ciRunId))
            throw new ArgumentException("CiRunId cannot be empty.", nameof(ciRunId));
        ArgumentNullException.ThrowIfNull(sourceRevision);

        Id = id;
        RepositoryId = repositoryId;
        CiJobName = ciJobName.Trim();
        CiBuildNumber = ciBuildNumber;
        CiRunUrl = ciRunUrl.Trim();
        CiRunId = ciRunId.Trim();
        SourceRevision = sourceRevision;
        TriggeredBy = string.IsNullOrWhiteSpace(triggeredBy) ? null : triggeredBy.Trim();
        Status = BuildStatus.Running;
        StartedAtUtc = startedAtUtc;

        RaiseEvent(new BuildStarted(
            Id, RepositoryId, CiJobName, CiBuildNumber, SourceRevision.CommitSha, startedAtUtc, startedAtUtc));
    }

    // --- Mid-build metadata ---

    public void RecordVersions(BuildVersions versions, DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(versions);
        if (Equals(Versions, versions)) return;
        Versions = versions;
        RaiseEvent(new BuildVersionsRecorded(Id, versions.PackageVersion, occurredAtUtc));
    }

    public void AttachQuality(BuildQuality quality, DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(quality);
        if (Equals(Quality, quality)) return;
        Quality = quality;
        RaiseEvent(new BuildQualityAttached(Id, quality.SbomUri, quality.VulnerabilityReportUri, occurredAtUtc));
    }

    // --- Artifacts & publications ---

    public BuildArtifact AddArtifact(
        Guid artifactId,
        ArtifactKind kind,
        string name,
        string version,
        string digest,
        long? sizeBytes,
        DateTimeOffset producedAtUtc)
    {
        if (_artifacts.Any(a => a.Kind == kind &&
                string.Equals(a.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.Version, version?.Trim(), StringComparison.Ordinal)))
            throw new InvalidOperationException(
                $"Artifact {kind} '{name}' v{version} already recorded on build {Id}.");

        var artifact = new BuildArtifact(artifactId, Id, kind, name!, version!, digest!, sizeBytes, producedAtUtc);
        _artifacts.Add(artifact);
        RaiseEvent(new ArtifactRecorded(
            Id, artifact.Id, kind, artifact.Name, artifact.Version, artifact.Digest, producedAtUtc));
        return artifact;
    }

    /// <summary>
    /// Record a registry push for one of this build's artifacts. For a successful
    /// container push, raises <see cref="ContainerPublished"/> so the auto-publish
    /// handler can consider a handoff.
    /// </summary>
    public ArtifactPublication AddPublication(
        Guid artifactId,
        Guid publicationId,
        PublicationRegistry registry,
        string reference,
        IEnumerable<string>? tags,
        PublicationStatus status,
        DateTimeOffset publishedAtUtc)
    {
        var artifact = _artifacts.FirstOrDefault(a => a.Id == artifactId)
            ?? throw new InvalidOperationException(
                $"Artifact {artifactId} not found on build {Id}.");

        var publication = artifact.AddPublication(publicationId, registry, reference, tags, status, publishedAtUtc);

        if (artifact.IsContainerImage &&
            registry == PublicationRegistry.NexusDocker &&
            status == PublicationStatus.Pushed)
        {
            RaiseEvent(new ContainerPublished(
                Id, artifact.Id, publication.Id, artifact.Name, publication.Reference,
                RepositoryId, Versions?.PackageVersion ?? string.Empty, SourceRevision.CommitSha,
                publishedAtUtc));
        }

        return publication;
    }

    /// <summary>
    /// Record that this Aspire build published its Kustomize-manifest archive to Nexus, raising
    /// <see cref="AspireManifestPublished"/> for the CI→deploy handoff. Idempotent: a repeat call
    /// with the same manifest URL is a no-op (the sync worker re-observes each tick).
    /// </summary>
    public void RecordAspireManifest(string appName, string manifestUrl, string version, DateTimeOffset occurredAtUtc)
    {
        if (string.IsNullOrWhiteSpace(appName))
            throw new ArgumentException("appName cannot be empty.", nameof(appName));
        if (string.IsNullOrWhiteSpace(manifestUrl))
            throw new ArgumentException("manifestUrl cannot be empty.", nameof(manifestUrl));

        var url = manifestUrl.Trim();
        if (string.Equals(AspireManifestUrl, url, StringComparison.Ordinal)) return;

        AspireManifestUrl = url;
        RaiseEvent(new AspireManifestPublished(
            Id, RepositoryId, appName.Trim(), url,
            version?.Trim() ?? string.Empty, SourceRevision.CommitSha, occurredAtUtc));
    }

    // --- Terminal transitions ---

    public void MarkSucceeded(DateTimeOffset completedAtUtc, long? durationMs)
    {
        TransitionTo(BuildStatus.Succeeded, completedAtUtc, durationMs);
        RaiseEvent(new BuildSucceeded(Id, completedAtUtc, completedAtUtc));
    }

    public void MarkFailed(DateTimeOffset completedAtUtc, long? durationMs)
    {
        TransitionTo(BuildStatus.Failed, completedAtUtc, durationMs);
        RaiseEvent(new BuildFailed(Id, completedAtUtc, completedAtUtc));
    }

    public void MarkAborted(DateTimeOffset completedAtUtc, long? durationMs)
    {
        TransitionTo(BuildStatus.Aborted, completedAtUtc, durationMs);
        RaiseEvent(new BuildAborted(Id, completedAtUtc, completedAtUtc));
    }

    private void TransitionTo(BuildStatus terminal, DateTimeOffset completedAtUtc, long? durationMs)
    {
        if (Status != BuildStatus.Running)
            throw new InvalidOperationException(
                $"Cannot move build {Id} to {terminal}: status is {Status}, expected Running.");

        Status = terminal;
        CompletedAtUtc = completedAtUtc;
        DurationMs = durationMs;
    }
}
