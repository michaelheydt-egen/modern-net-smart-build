using Deployment.Contracts.Runs;

namespace Deployment.Application.Features.Containers;

public interface IKnownContainerReader
{
    Task<IReadOnlyList<KnownContainerDto>> ListAsync(CancellationToken ct = default);
}

public sealed record ListKnownContainersQuery;
public sealed class ListKnownContainersHandler
{
    private readonly IKnownContainerReader _reader;
    public ListKnownContainersHandler(IKnownContainerReader reader) => _reader = reader;
    public Task<IReadOnlyList<KnownContainerDto>> HandleAsync(ListKnownContainersQuery q, CancellationToken ct = default) => _reader.ListAsync(ct);
}
