using Jenkins.Domain.Common;
using Jenkins.Domain.SourceRepositories.Events;

namespace Jenkins.Domain.SourceRepositories;

/// <summary>
/// Aggregate root for a tracked source-control repository — the first-class
/// "what is being built" concept (handoff §3). Owns the CI identity (which
/// Jenkins job builds it, the BASE_VER that seeds versions) and the set of
/// <see cref="DeployableComponent"/> mappings that wire its container images to
/// deployment Services.
///
/// Named <c>SourceRepository</c> (not <c>Repository</c>) to avoid colliding with
/// the persistence repository pattern. A repo with zero components simply isn't
/// integrated with deployment.
/// </summary>
public sealed class SourceRepository : AggregateRoot<Guid>
{
    public string Name { get; private set; }
    public string GitUrl { get; private set; }
    public RepositoryProvider Provider { get; private set; }
    public string DefaultBranch { get; private set; }

    /// <summary>The Jenkins job that builds this repo (e.g. <c>cicd-build</c>).</summary>
    public string CiJobName { get; private set; }

    /// <summary>The <c>BASE_VER</c> parameter that seeds version derivation.</summary>
    public string BaseVersion { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>
    /// Per-repo gate on producing container images (the "combination" with the code-level
    /// opt-in: a project declares it is containerizable via the <c>Containerizable</c> MSBuild
    /// property, and the repo must also permit it). Default <c>true</c> so existing repos keep
    /// producing containers; set <c>false</c> to suppress the container-publish stage for a repo
    /// that should only ship NuGet packages. Does not affect NuGet publishing or the
    /// <see cref="DeployableComponent"/> deployment mapping.
    /// </summary>
    public bool AllowContainerPublish { get; private set; }

    /// <summary>How this repo is built: the standard chain, or a .NET Aspire app via Aspir8.</summary>
    public BuildKind BuildKind { get; private set; }

    /// <summary>For an Aspire repo: path (within the repo) to the AppHost dir/csproj; null = auto-discover.</summary>
    public string? AppHostPath { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    private readonly List<DeployableComponent> _components = new();
    public IReadOnlyCollection<DeployableComponent> Components => _components.AsReadOnly();

    private SourceRepository()
    {
        Name = string.Empty;
        GitUrl = string.Empty;
        DefaultBranch = string.Empty;
        CiJobName = string.Empty;
        BaseVersion = string.Empty;
        AllowContainerPublish = true;
    }

    public SourceRepository(
        Guid id,
        string name,
        string gitUrl,
        RepositoryProvider provider,
        string defaultBranch,
        string ciJobName,
        string baseVersion,
        DateTimeOffset createdAtUtc,
        BuildKind buildKind = BuildKind.Standard,
        string? appHostPath = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id cannot be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(gitUrl))
            throw new ArgumentException("GitUrl cannot be empty.", nameof(gitUrl));
        if (string.IsNullOrWhiteSpace(defaultBranch))
            throw new ArgumentException("DefaultBranch cannot be empty.", nameof(defaultBranch));
        if (string.IsNullOrWhiteSpace(ciJobName))
            throw new ArgumentException("CiJobName cannot be empty.", nameof(ciJobName));
        if (string.IsNullOrWhiteSpace(baseVersion))
            throw new ArgumentException("BaseVersion cannot be empty.", nameof(baseVersion));

        Id = id;
        Name = name.Trim();
        GitUrl = gitUrl.Trim();
        Provider = provider;
        DefaultBranch = defaultBranch.Trim();
        CiJobName = ciJobName.Trim();
        BaseVersion = baseVersion.Trim();
        IsActive = true;
        AllowContainerPublish = true;
        BuildKind = buildKind;
        AppHostPath = Clean(appHostPath);
        CreatedAtUtc = createdAtUtc;

        RaiseEvent(new RepositoryRegistered(
            Id, Name, GitUrl, Provider, DefaultBranch, CiJobName, BaseVersion, createdAtUtc));
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    // --- Details ---

    /// <summary>
    /// Update the editable identity/CI fields. Same guards as the ctor. The
    /// unique-name invariant (vs. <em>other</em> repos) is enforced by the handler.
    /// </summary>
    public void UpdateDetails(
        string name,
        string gitUrl,
        RepositoryProvider provider,
        string defaultBranch,
        string ciJobName,
        string baseVersion,
        DateTimeOffset occurredAtUtc,
        BuildKind buildKind = BuildKind.Standard,
        string? appHostPath = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(gitUrl))
            throw new ArgumentException("GitUrl cannot be empty.", nameof(gitUrl));
        if (string.IsNullOrWhiteSpace(defaultBranch))
            throw new ArgumentException("DefaultBranch cannot be empty.", nameof(defaultBranch));
        if (string.IsNullOrWhiteSpace(ciJobName))
            throw new ArgumentException("CiJobName cannot be empty.", nameof(ciJobName));
        if (string.IsNullOrWhiteSpace(baseVersion))
            throw new ArgumentException("BaseVersion cannot be empty.", nameof(baseVersion));

        Name = name.Trim();
        GitUrl = gitUrl.Trim();
        Provider = provider;
        DefaultBranch = defaultBranch.Trim();
        CiJobName = ciJobName.Trim();
        BaseVersion = baseVersion.Trim();
        BuildKind = buildKind;
        AppHostPath = Clean(appHostPath);

        RaiseEvent(new RepositoryDetailsUpdated(
            Id, Name, GitUrl, Provider, DefaultBranch, CiJobName, BaseVersion, occurredAtUtc));
    }

    // --- Deployment-component mappings ---

    /// <summary>
    /// Map a container image this repo produces to a deployment Service. Container
    /// names are unique within a repo; remap an existing one via
    /// <see cref="RemapComponent"/>.
    /// </summary>
    public DeployableComponent AddComponent(
        Guid componentId,
        string containerName,
        Guid deployableUnitId,
        string deployableUnitName,
        bool autoPublish,
        DateTimeOffset occurredAtUtc)
    {
        if (_components.Any(c => string.Equals(c.ContainerName, containerName?.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"Container '{containerName}' is already mapped on repository {Id}.");

        var component = new DeployableComponent(
            componentId, Id, containerName!, deployableUnitId, deployableUnitName, autoPublish);
        _components.Add(component);
        RaiseEvent(new DeployableComponentAdded(
            Id, component.Id, component.ContainerName, deployableUnitId, autoPublish, occurredAtUtc));
        return component;
    }

    public void RemapComponent(
        Guid componentId,
        Guid deployableUnitId,
        string deployableUnitName,
        bool autoPublish,
        DateTimeOffset occurredAtUtc)
    {
        var component = _components.FirstOrDefault(c => c.Id == componentId)
            ?? throw new InvalidOperationException(
                $"Component {componentId} not found on repository {Id}.");

        if (component.Remap(deployableUnitId, deployableUnitName, autoPublish))
        {
            RaiseEvent(new DeployableComponentRemapped(
                Id, component.Id, deployableUnitId, autoPublish, occurredAtUtc));
        }
    }

    public void DeactivateComponent(Guid componentId, DateTimeOffset occurredAtUtc)
    {
        var component = _components.FirstOrDefault(c => c.Id == componentId)
            ?? throw new InvalidOperationException(
                $"Component {componentId} not found on repository {Id}.");

        if (!component.IsActive) return;
        component.Deactivate();
        RaiseEvent(new DeployableComponentDeactivated(Id, component.Id, occurredAtUtc));
    }

    /// <summary>
    /// Resolve the active component (if any) whose <c>ContainerName</c> matches a
    /// produced artifact name — the join used at handoff time.
    /// </summary>
    public DeployableComponent? MatchComponent(string containerName) =>
        _components.FirstOrDefault(c =>
            c.IsActive && string.Equals(c.ContainerName, containerName?.Trim(), StringComparison.OrdinalIgnoreCase));

    // --- Container-publish gate ---

    /// <summary>
    /// Allow or suppress container production for this repo (see
    /// <see cref="AllowContainerPublish"/>). No-op if already at the requested value.
    /// </summary>
    public void SetContainerPublishAllowed(bool allowed, DateTimeOffset occurredAtUtc)
    {
        if (AllowContainerPublish == allowed) return;
        AllowContainerPublish = allowed;
        RaiseEvent(new RepositoryContainerPublishAllowedChanged(Id, allowed, occurredAtUtc));
    }

    // --- Activation ---

    public void Deactivate(DateTimeOffset occurredAtUtc)
    {
        if (!IsActive) return;
        IsActive = false;
        RaiseEvent(new RepositoryDeactivated(Id, occurredAtUtc));
    }

    public void Reactivate(DateTimeOffset occurredAtUtc)
    {
        if (IsActive) return;
        IsActive = true;
        RaiseEvent(new RepositoryReactivated(Id, occurredAtUtc));
    }
}
