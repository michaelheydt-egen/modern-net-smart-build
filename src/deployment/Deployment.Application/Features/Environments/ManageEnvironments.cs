using FluentValidation;
using Deployment.Contracts.Catalog;
using Deployment.Domain.Abstractions;
using Deployment.Domain.Environments;
using Deployment.Domain.Mappings;

namespace Deployment.Application.Features.Environments;

internal static class EnvironmentMapping
{
    public static EnvironmentDto ToDto(this DeploymentEnvironment e) =>
        new(e.Id, e.Name, e.GcpProject, e.Region, e.GarRepository, e.IsActive, e.CreatedAtUtc, e.UpdatedAtUtc);
}

public sealed record CreateEnvironmentCommand(string Name, string GcpProject, string Region, string GarRepository);

public sealed class CreateEnvironmentValidator : AbstractValidator<CreateEnvironmentCommand>
{
    public CreateEnvironmentValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.GcpProject).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Region).NotEmpty().MaximumLength(100);
        RuleFor(x => x.GarRepository).MaximumLength(200);
    }
}

public sealed class CreateEnvironmentHandler
{
    private readonly IEnvironmentRepository _envs;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    public CreateEnvironmentHandler(IEnvironmentRepository envs, IUnitOfWork uow, TimeProvider clock)
    { _envs = envs; _uow = uow; _clock = clock; }

    public async Task<EnvironmentDto> HandleAsync(CreateEnvironmentCommand cmd, CancellationToken ct = default)
    {
        if (await _envs.FindByNameAsync(cmd.Name, ct).ConfigureAwait(false) is not null)
            throw new InvalidOperationException($"An environment named '{cmd.Name}' already exists.");
        var env = new DeploymentEnvironment(Guid.NewGuid(), cmd.Name, cmd.GcpProject, cmd.Region, cmd.GarRepository, _clock.GetUtcNow());
        await _envs.AddAsync(env, ct).ConfigureAwait(false);
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
        return env.ToDto();
    }
}

public sealed record UpdateEnvironmentCommand(Guid EnvironmentId, string Name, string GcpProject, string Region, string GarRepository);

public sealed class UpdateEnvironmentValidator : AbstractValidator<UpdateEnvironmentCommand>
{
    public UpdateEnvironmentValidator()
    {
        RuleFor(x => x.EnvironmentId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.GcpProject).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Region).NotEmpty().MaximumLength(100);
    }
}

public sealed class UpdateEnvironmentHandler
{
    private readonly IEnvironmentRepository _envs;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    public UpdateEnvironmentHandler(IEnvironmentRepository envs, IUnitOfWork uow, TimeProvider clock)
    { _envs = envs; _uow = uow; _clock = clock; }

    public async Task HandleAsync(UpdateEnvironmentCommand cmd, CancellationToken ct = default)
    {
        var env = await _envs.GetByIdAsync(cmd.EnvironmentId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Environment {cmd.EnvironmentId} not found.");
        env.Update(cmd.Name, cmd.GcpProject, cmd.Region, cmd.GarRepository, _clock.GetUtcNow());
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

public sealed record DeleteEnvironmentCommand(Guid EnvironmentId);

public sealed class DeleteEnvironmentHandler
{
    private readonly IEnvironmentRepository _envs;
    private readonly IDeploymentMappingRepository _mappings;
    private readonly IUnitOfWork _uow;
    public DeleteEnvironmentHandler(IEnvironmentRepository envs, IDeploymentMappingRepository mappings, IUnitOfWork uow)
    { _envs = envs; _mappings = mappings; _uow = uow; }

    public async Task HandleAsync(DeleteEnvironmentCommand cmd, CancellationToken ct = default)
    {
        var env = await _envs.GetByIdAsync(cmd.EnvironmentId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Environment {cmd.EnvironmentId} not found.");
        var maps = await _mappings.ListByEnvironmentAsync(env.Id, ct).ConfigureAwait(false);
        if (maps.Count > 0)
            throw new InvalidOperationException($"Environment '{env.Name}' is referenced by {maps.Count} mapping(s); remove them first.");
        _envs.Remove(env);
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
