using Publisher.Domain.Abstractions;
using Publisher.Domain.Containers;

namespace Publisher.Application.Features.Containers;

/// <summary>
/// Removes a container from the inventory. Promotion history (audit) and any channels that
/// referenced it are left intact — a channel pointing at a deleted container simply resolves to
/// nothing on its next publish. Returns false if the container was not found.
/// </summary>
public sealed record DeleteContainerCommand(Guid ContainerId);

public sealed class DeleteContainerHandler
{
    private readonly IPublishableContainerRepository _containers;
    private readonly IUnitOfWork _uow;

    public DeleteContainerHandler(IPublishableContainerRepository containers, IUnitOfWork uow)
    {
        _containers = containers;
        _uow = uow;
    }

    public async Task<bool> HandleAsync(DeleteContainerCommand cmd, CancellationToken cancellationToken = default)
    {
        var container = await _containers.GetByIdAsync(cmd.ContainerId, cancellationToken).ConfigureAwait(false);
        if (container is null) return false;

        _containers.Remove(container);
        await _uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
