using FluentValidation;
using Deployment.Contracts.Mappings;
using Deployment.Domain.Abstractions;
using Deployment.Domain.Environments;
using Deployment.Domain.Mappings;
using Deployment.Domain.Services;

namespace Deployment.Application.Features.Mappings;

internal static class MappingConvert
{
    public static IReadOnlyList<DeploymentStep>? ToDomain(this IReadOnlyList<DeploymentStepDto>? steps) =>
        steps?.Select(s => new DeploymentStep(s.Order, (DeploymentStepKind)(int)s.Kind, new Dictionary<string, string>())).ToList();
}

public sealed record CreateMappingCommand(Guid ServiceId, Guid EnvironmentId, string CloudRunServiceName, bool AutoDeploy, IReadOnlyList<DeploymentStepDto>? Steps);

public sealed class CreateMappingValidator : AbstractValidator<CreateMappingCommand>
{
    public CreateMappingValidator()
    {
        RuleFor(x => x.ServiceId).NotEmpty();
        RuleFor(x => x.EnvironmentId).NotEmpty();
        RuleFor(x => x.CloudRunServiceName).NotEmpty().MaximumLength(300);
    }
}

public sealed class CreateMappingHandler
{
    private readonly IDeploymentMappingRepository _mappings;
    private readonly IServiceRepository _services;
    private readonly IEnvironmentRepository _envs;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    public CreateMappingHandler(IDeploymentMappingRepository mappings, IServiceRepository services, IEnvironmentRepository envs, IUnitOfWork uow, TimeProvider clock)
    { _mappings = mappings; _services = services; _envs = envs; _uow = uow; _clock = clock; }

    public async Task<Guid> HandleAsync(CreateMappingCommand cmd, CancellationToken ct = default)
    {
        if (await _services.GetByIdAsync(cmd.ServiceId, ct).ConfigureAwait(false) is null)
            throw new InvalidOperationException($"Service {cmd.ServiceId} does not exist.");
        if (await _envs.GetByIdAsync(cmd.EnvironmentId, ct).ConfigureAwait(false) is null)
            throw new InvalidOperationException($"Environment {cmd.EnvironmentId} does not exist.");
        if (await _mappings.FindAsync(cmd.ServiceId, cmd.EnvironmentId, ct).ConfigureAwait(false) is not null)
            throw new InvalidOperationException("A mapping for this service + environment already exists.");

        var mapping = new DeploymentMapping(
            Guid.NewGuid(), cmd.ServiceId, cmd.EnvironmentId, cmd.CloudRunServiceName,
            cmd.AutoDeploy, cmd.Steps.ToDomain(), _clock.GetUtcNow());
        await _mappings.AddAsync(mapping, ct).ConfigureAwait(false);
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
        return mapping.Id;
    }
}

public sealed record UpdateMappingCommand(Guid MappingId, string CloudRunServiceName, IReadOnlyList<DeploymentStepDto>? Steps);

public sealed class UpdateMappingValidator : AbstractValidator<UpdateMappingCommand>
{
    public UpdateMappingValidator()
    {
        RuleFor(x => x.MappingId).NotEmpty();
        RuleFor(x => x.CloudRunServiceName).NotEmpty().MaximumLength(300);
    }
}

public sealed class UpdateMappingHandler
{
    private readonly IDeploymentMappingRepository _mappings;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    public UpdateMappingHandler(IDeploymentMappingRepository mappings, IUnitOfWork uow, TimeProvider clock)
    { _mappings = mappings; _uow = uow; _clock = clock; }

    public async Task HandleAsync(UpdateMappingCommand cmd, CancellationToken ct = default)
    {
        var mapping = await _mappings.GetByIdAsync(cmd.MappingId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Mapping {cmd.MappingId} not found.");
        mapping.Update(cmd.CloudRunServiceName, cmd.Steps.ToDomain(), _clock.GetUtcNow());
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

public sealed record SetAutoDeployCommand(Guid MappingId, bool AutoDeploy);

public sealed class SetAutoDeployHandler
{
    private readonly IDeploymentMappingRepository _mappings;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    public SetAutoDeployHandler(IDeploymentMappingRepository mappings, IUnitOfWork uow, TimeProvider clock)
    { _mappings = mappings; _uow = uow; _clock = clock; }

    public async Task HandleAsync(SetAutoDeployCommand cmd, CancellationToken ct = default)
    {
        var mapping = await _mappings.GetByIdAsync(cmd.MappingId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Mapping {cmd.MappingId} not found.");
        mapping.SetAutoDeploy(cmd.AutoDeploy, _clock.GetUtcNow());
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

public sealed record DeleteMappingCommand(Guid MappingId);

public sealed class DeleteMappingHandler
{
    private readonly IDeploymentMappingRepository _mappings;
    private readonly IUnitOfWork _uow;
    public DeleteMappingHandler(IDeploymentMappingRepository mappings, IUnitOfWork uow)
    { _mappings = mappings; _uow = uow; }

    public async Task HandleAsync(DeleteMappingCommand cmd, CancellationToken ct = default)
    {
        var mapping = await _mappings.GetByIdAsync(cmd.MappingId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Mapping {cmd.MappingId} not found.");
        _mappings.Remove(mapping);
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
