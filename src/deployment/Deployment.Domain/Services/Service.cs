using Deployment.Domain.Common;
using Deployment.Domain.Services.Events;

namespace Deployment.Domain.Services;

/// <summary>
/// A deployable unit: a name + the Nexus container (by name) it deploys. Mapped to one or more
/// environments via <see cref="Mappings.DeploymentMapping"/>.
/// </summary>
public sealed class Service : AggregateRoot<Guid>
{
    public string Name { get; private set; }

    /// <summary>The container image name (as it appears in the local Nexus docker registry), e.g. <c>webapphost</c>.</summary>
    public string ContainerName { get; private set; }

    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private Service()
    {
        Name = string.Empty;
        ContainerName = string.Empty;
    }

    public Service(Guid id, string name, string containerName, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id cannot be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name cannot be empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(containerName)) throw new ArgumentException("ContainerName cannot be empty.", nameof(containerName));

        Id = id;
        Name = name.Trim();
        ContainerName = containerName.Trim();
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;

        RaiseEvent(new ServiceRegistered(Id, Name, ContainerName, createdAtUtc));
    }

    public void Update(string name, string containerName, DateTimeOffset occurredAtUtc)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name cannot be empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(containerName)) throw new ArgumentException("ContainerName cannot be empty.", nameof(containerName));
        Name = name.Trim();
        ContainerName = containerName.Trim();
        UpdatedAtUtc = occurredAtUtc;
        RaiseEvent(new ServiceUpdated(Id, Name, ContainerName, occurredAtUtc));
    }

    public void ChangeActivation(bool active, DateTimeOffset occurredAtUtc)
    {
        if (IsActive == active) return;
        IsActive = active;
        UpdatedAtUtc = occurredAtUtc;
    }
}
