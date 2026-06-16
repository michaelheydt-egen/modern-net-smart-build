using FluentValidation;
using Deployment.Contracts.Catalog;
using Deployment.Domain.Abstractions;
using Deployment.Domain.Mappings;
using Deployment.Domain.Services;

namespace Deployment.Application.Features.Services;

internal static class ServiceMapping
{
    public static ServiceDto ToDto(this Service s) =>
        new(s.Id, s.Name, s.ContainerName, s.IsActive, s.CreatedAtUtc, s.UpdatedAtUtc);
}

public sealed record CreateServiceCommand(string Name, string ContainerName);

public sealed class CreateServiceValidator : AbstractValidator<CreateServiceCommand>
{
    public CreateServiceValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContainerName).NotEmpty().MaximumLength(300);
    }
}

public sealed class CreateServiceHandler
{
    private readonly IServiceRepository _services;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    public CreateServiceHandler(IServiceRepository services, IUnitOfWork uow, TimeProvider clock)
    { _services = services; _uow = uow; _clock = clock; }

    public async Task<ServiceDto> HandleAsync(CreateServiceCommand cmd, CancellationToken ct = default)
    {
        if (await _services.FindByNameAsync(cmd.Name, ct).ConfigureAwait(false) is not null)
            throw new InvalidOperationException($"A service named '{cmd.Name}' already exists.");
        var service = new Service(Guid.NewGuid(), cmd.Name, cmd.ContainerName, _clock.GetUtcNow());
        await _services.AddAsync(service, ct).ConfigureAwait(false);
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
        return service.ToDto();
    }
}

public sealed record UpdateServiceCommand(Guid ServiceId, string Name, string ContainerName);

public sealed class UpdateServiceValidator : AbstractValidator<UpdateServiceCommand>
{
    public UpdateServiceValidator()
    {
        RuleFor(x => x.ServiceId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContainerName).NotEmpty().MaximumLength(300);
    }
}

public sealed class UpdateServiceHandler
{
    private readonly IServiceRepository _services;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    public UpdateServiceHandler(IServiceRepository services, IUnitOfWork uow, TimeProvider clock)
    { _services = services; _uow = uow; _clock = clock; }

    public async Task HandleAsync(UpdateServiceCommand cmd, CancellationToken ct = default)
    {
        var service = await _services.GetByIdAsync(cmd.ServiceId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Service {cmd.ServiceId} not found.");
        service.Update(cmd.Name, cmd.ContainerName, _clock.GetUtcNow());
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

public sealed record ChangeServiceActivationCommand(Guid ServiceId, bool Active);

public sealed class ChangeServiceActivationHandler
{
    private readonly IServiceRepository _services;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    public ChangeServiceActivationHandler(IServiceRepository services, IUnitOfWork uow, TimeProvider clock)
    { _services = services; _uow = uow; _clock = clock; }

    public async Task HandleAsync(ChangeServiceActivationCommand cmd, CancellationToken ct = default)
    {
        var service = await _services.GetByIdAsync(cmd.ServiceId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Service {cmd.ServiceId} not found.");
        service.ChangeActivation(cmd.Active, _clock.GetUtcNow());
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

public sealed record DeleteServiceCommand(Guid ServiceId);

public sealed class DeleteServiceHandler
{
    private readonly IServiceRepository _services;
    private readonly IDeploymentMappingRepository _mappings;
    private readonly IUnitOfWork _uow;
    public DeleteServiceHandler(IServiceRepository services, IDeploymentMappingRepository mappings, IUnitOfWork uow)
    { _services = services; _mappings = mappings; _uow = uow; }

    public async Task HandleAsync(DeleteServiceCommand cmd, CancellationToken ct = default)
    {
        var service = await _services.GetByIdAsync(cmd.ServiceId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Service {cmd.ServiceId} not found.");
        var maps = await _mappings.ListByServiceAsync(service.Id, ct).ConfigureAwait(false);
        if (maps.Count > 0)
            throw new InvalidOperationException($"Service '{service.Name}' has {maps.Count} mapping(s); remove them first.");
        _services.Remove(service);
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
