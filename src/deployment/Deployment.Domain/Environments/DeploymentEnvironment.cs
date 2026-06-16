using Deployment.Domain.Common;
using Deployment.Domain.Environments.Events;

namespace Deployment.Domain.Environments;

/// <summary>
/// A deployment target environment (dev/staging/prod) with its Google Cloud coordinates. The
/// "where": a service is deployed to one or more of these via a mapping.
/// (Named DeploymentEnvironment to avoid clashing with System.Environment.)
/// </summary>
public sealed class DeploymentEnvironment : AggregateRoot<Guid>
{
    public string Name { get; private set; }

    /// <summary>GCP project id, e.g. <c>my-project</c>.</summary>
    public string GcpProject { get; private set; }

    /// <summary>GCP region, e.g. <c>us-central1</c> (Cloud Run location + GAR location).</summary>
    public string Region { get; private set; }

    /// <summary>Google Artifact Registry repository name the container is promoted into.</summary>
    public string GarRepository { get; private set; }

    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private DeploymentEnvironment()
    {
        Name = string.Empty;
        GcpProject = string.Empty;
        Region = string.Empty;
        GarRepository = string.Empty;
    }

    public DeploymentEnvironment(Guid id, string name, string gcpProject, string region, string garRepository, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id cannot be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name cannot be empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(gcpProject)) throw new ArgumentException("GcpProject cannot be empty.", nameof(gcpProject));
        if (string.IsNullOrWhiteSpace(region)) throw new ArgumentException("Region cannot be empty.", nameof(region));

        Id = id;
        Name = name.Trim();
        GcpProject = gcpProject.Trim();
        Region = region.Trim();
        GarRepository = garRepository?.Trim() ?? string.Empty;
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;

        RaiseEvent(new EnvironmentRegistered(Id, Name, GcpProject, Region, createdAtUtc));
    }

    public void Update(string name, string gcpProject, string region, string garRepository, DateTimeOffset occurredAtUtc)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name cannot be empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(gcpProject)) throw new ArgumentException("GcpProject cannot be empty.", nameof(gcpProject));
        if (string.IsNullOrWhiteSpace(region)) throw new ArgumentException("Region cannot be empty.", nameof(region));
        Name = name.Trim();
        GcpProject = gcpProject.Trim();
        Region = region.Trim();
        GarRepository = garRepository?.Trim() ?? string.Empty;
        UpdatedAtUtc = occurredAtUtc;
        RaiseEvent(new EnvironmentUpdated(Id, Name, GcpProject, Region, occurredAtUtc));
    }

    public void ChangeActivation(bool active, DateTimeOffset occurredAtUtc)
    {
        if (IsActive == active) return;
        IsActive = active;
        UpdatedAtUtc = occurredAtUtc;
    }
}
