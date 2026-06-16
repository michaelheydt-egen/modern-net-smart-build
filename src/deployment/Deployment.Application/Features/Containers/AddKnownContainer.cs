using FluentValidation;
using Deployment.Contracts.Runs;
using Deployment.Domain.Abstractions;
using Deployment.Domain.Containers;

namespace Deployment.Application.Features.Containers;

/// <summary>
/// Manually records a container in the light inventory — the same upsert the bus consumer does,
/// exposed for testing the deploy flow without a live CI push. Upsert by container name.
/// </summary>
public sealed record AddKnownContainerCommand(string ContainerName, string Version, string NexusRef);

public sealed class AddKnownContainerValidator : AbstractValidator<AddKnownContainerCommand>
{
    public AddKnownContainerValidator()
    {
        RuleFor(x => x.ContainerName).NotEmpty().MaximumLength(300);
        RuleFor(x => x.NexusRef).NotEmpty().MaximumLength(1000);
    }
}

public sealed class AddKnownContainerHandler
{
    private readonly IKnownContainerRepository _containers;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    public AddKnownContainerHandler(IKnownContainerRepository containers, IUnitOfWork uow, TimeProvider clock)
    { _containers = containers; _uow = uow; _clock = clock; }

    public async Task<KnownContainerDto> HandleAsync(AddKnownContainerCommand cmd, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        var known = await _containers.FindByNameAsync(cmd.ContainerName, ct).ConfigureAwait(false);
        if (known is null)
        {
            known = new KnownContainer(Guid.NewGuid(), cmd.ContainerName, cmd.Version, cmd.NexusRef, now);
            await _containers.AddAsync(known, ct).ConfigureAwait(false);
        }
        else
        {
            known.Observe(cmd.Version, cmd.NexusRef, now);
        }
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
        return new KnownContainerDto(known.Id, known.ContainerName, known.Version, known.ImageDigest, known.NexusRef, known.FirstSeenAtUtc, known.LastSeenAtUtc);
    }
}
