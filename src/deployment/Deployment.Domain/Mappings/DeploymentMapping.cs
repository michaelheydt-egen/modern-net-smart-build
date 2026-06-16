using Deployment.Domain.Common;
using Deployment.Domain.Mappings.Events;

namespace Deployment.Domain.Mappings;

/// <summary>
/// The service↔environment edge + recipe: how (and whether automatically) a service is deployed
/// to a given environment. Unique per (Service, Environment). Holds the ordered, pluggable step
/// list and the Cloud Run service name to target.
/// </summary>
public sealed class DeploymentMapping : AggregateRoot<Guid>
{
    public Guid ServiceId { get; private set; }
    public Guid EnvironmentId { get; private set; }

    /// <summary>The Cloud Run service to create/update in the environment's project+region.</summary>
    public string CloudRunServiceName { get; private set; }

    /// <summary>When true, a matching ContainerPublished event auto-triggers a deployment.</summary>
    public bool AutoDeploy { get; private set; }

    /// <summary>Ordered recipe. Defaults to GarPush → CloudRunDeploy.</summary>
    public IReadOnlyList<DeploymentStep> Steps { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private DeploymentMapping()
    {
        CloudRunServiceName = string.Empty;
        Steps = Array.Empty<DeploymentStep>();
    }

    public DeploymentMapping(
        Guid id, Guid serviceId, Guid environmentId, string cloudRunServiceName,
        bool autoDeploy, IReadOnlyList<DeploymentStep>? steps, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id cannot be empty.", nameof(id));
        if (serviceId == Guid.Empty) throw new ArgumentException("ServiceId cannot be empty.", nameof(serviceId));
        if (environmentId == Guid.Empty) throw new ArgumentException("EnvironmentId cannot be empty.", nameof(environmentId));
        if (string.IsNullOrWhiteSpace(cloudRunServiceName)) throw new ArgumentException("CloudRunServiceName cannot be empty.", nameof(cloudRunServiceName));

        Id = id;
        ServiceId = serviceId;
        EnvironmentId = environmentId;
        CloudRunServiceName = cloudRunServiceName.Trim();
        AutoDeploy = autoDeploy;
        Steps = NormalizeOrDefault(steps);
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;

        RaiseEvent(new DeploymentMappingCreated(Id, ServiceId, EnvironmentId, AutoDeploy, createdAtUtc));
    }

    public void Update(string cloudRunServiceName, IReadOnlyList<DeploymentStep>? steps, DateTimeOffset occurredAtUtc)
    {
        if (string.IsNullOrWhiteSpace(cloudRunServiceName)) throw new ArgumentException("CloudRunServiceName cannot be empty.", nameof(cloudRunServiceName));
        CloudRunServiceName = cloudRunServiceName.Trim();
        Steps = NormalizeOrDefault(steps);
        UpdatedAtUtc = occurredAtUtc;
        RaiseEvent(new DeploymentMappingUpdated(Id, ServiceId, EnvironmentId, AutoDeploy, occurredAtUtc));
    }

    public void SetAutoDeploy(bool autoDeploy, DateTimeOffset occurredAtUtc)
    {
        if (AutoDeploy == autoDeploy) return;
        AutoDeploy = autoDeploy;
        UpdatedAtUtc = occurredAtUtc;
        RaiseEvent(new DeploymentMappingAutoDeployChanged(Id, autoDeploy, occurredAtUtc));
    }

    /// <summary>The built-in default recipe: promote to GAR, then deploy to Cloud Run.</summary>
    public static IReadOnlyList<DeploymentStep> DefaultSteps() => new[]
    {
        DeploymentStep.Of(1, DeploymentStepKind.GarPush),
        DeploymentStep.Of(2, DeploymentStepKind.CloudRunDeploy),
    };

    private static IReadOnlyList<DeploymentStep> NormalizeOrDefault(IReadOnlyList<DeploymentStep>? steps)
    {
        if (steps is null || steps.Count == 0) return DefaultSteps();
        return steps.OrderBy(s => s.Order)
            .Select((s, i) => s with { Order = i + 1 })
            .ToArray();
    }
}
