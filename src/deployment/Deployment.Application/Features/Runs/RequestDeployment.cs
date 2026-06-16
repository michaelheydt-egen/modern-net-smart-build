using Microsoft.Extensions.Logging;
using Deployment.Domain.Abstractions;
using Deployment.Domain.Containers;
using Deployment.Domain.Environments;
using Deployment.Domain.Mappings;
using Deployment.Domain.Runs;
using Deployment.Domain.Services;

namespace Deployment.Application.Features.Runs;

/// <summary>
/// Creates a <see cref="DeploymentRun"/> (Pending) for a mapping — the shared path for both the
/// manual API trigger and the auto (event-driven) trigger. Resolves the service, environment, and
/// the container to deploy (a specific version if given, else the latest known), snapshots the
/// target coordinates onto the run, and saves it. The run's <c>DeploymentRunRequested</c> domain
/// event drives <see cref="DeploymentRunExecutor"/>.
/// </summary>
public sealed record RequestDeploymentCommand(Guid MappingId, string? Version, DeploymentTrigger Trigger, string? TriggeredBy);

public sealed record RequestDeploymentResult(Guid? RunId, string Outcome);

public sealed class RequestDeploymentHandler
{
    private readonly IDeploymentMappingRepository _mappings;
    private readonly IServiceRepository _services;
    private readonly IEnvironmentRepository _environments;
    private readonly IKnownContainerRepository _containers;
    private readonly IDeploymentRunRepository _runs;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    private readonly ILogger<RequestDeploymentHandler> _logger;

    public RequestDeploymentHandler(
        IDeploymentMappingRepository mappings, IServiceRepository services, IEnvironmentRepository environments,
        IKnownContainerRepository containers, IDeploymentRunRepository runs, IUnitOfWork uow,
        TimeProvider clock, ILogger<RequestDeploymentHandler> logger)
    {
        _mappings = mappings;
        _services = services;
        _environments = environments;
        _containers = containers;
        _runs = runs;
        _uow = uow;
        _clock = clock;
        _logger = logger;
    }

    public async Task<RequestDeploymentResult> HandleAsync(RequestDeploymentCommand cmd, CancellationToken ct = default)
    {
        var mapping = await _mappings.GetByIdAsync(cmd.MappingId, ct).ConfigureAwait(false);
        if (mapping is null) return new RequestDeploymentResult(null, "mapping-not-found");

        var service = await _services.GetByIdAsync(mapping.ServiceId, ct).ConfigureAwait(false);
        if (service is null) return new RequestDeploymentResult(null, "service-not-found");

        var environment = await _environments.GetByIdAsync(mapping.EnvironmentId, ct).ConfigureAwait(false);
        if (environment is null) return new RequestDeploymentResult(null, "environment-not-found");

        var container = await _containers.FindByNameAsync(service.ContainerName, ct).ConfigureAwait(false);
        if (container is null)
            return new RequestDeploymentResult(null, "no-known-container");

        // A specific version was requested but the latest-known differs — for now we deploy what we
        // know (the inventory tracks the latest push). Versioned history is an extension.
        var version = string.IsNullOrWhiteSpace(cmd.Version) ? container.Version : cmd.Version!.Trim();

        var run = new DeploymentRun(
            id: Guid.NewGuid(),
            mappingId: mapping.Id,
            serviceId: service.Id,
            environmentId: environment.Id,
            serviceName: service.Name,
            containerName: service.ContainerName,
            version: version,
            sourceRef: container.NexusRef,
            gcpProject: environment.GcpProject,
            region: environment.Region,
            garRepository: environment.GarRepository,
            cloudRunServiceName: mapping.CloudRunServiceName,
            trigger: cmd.Trigger,
            triggeredBy: cmd.TriggeredBy ?? (cmd.Trigger == DeploymentTrigger.Auto ? "auto" : "manual"),
            requestedAtUtc: _clock.GetUtcNow());

        await _runs.AddAsync(run, ct).ConfigureAwait(false);
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("[deploy] Requested {Trigger} deployment of {Service} ({Container} {Version}) -> {Service2}/{Env} (run {Run}).",
            cmd.Trigger, service.Name, service.ContainerName, version, service.Name, environment.Name, run.Id);
        return new RequestDeploymentResult(run.Id, "requested");
    }
}
