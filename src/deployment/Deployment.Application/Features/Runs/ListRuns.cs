using Deployment.Contracts.Runs;

namespace Deployment.Application.Features.Runs;

public interface IRunReader
{
    Task<IReadOnlyList<DeploymentRunDto>> ListAsync(Guid? serviceId, Guid? mappingId, CancellationToken ct = default);
    Task<DeploymentRunDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
}

public sealed record ListRunsQuery(Guid? ServiceId, Guid? MappingId);
public sealed class ListRunsHandler
{
    private readonly IRunReader _reader;
    public ListRunsHandler(IRunReader reader) => _reader = reader;
    public Task<IReadOnlyList<DeploymentRunDto>> HandleAsync(ListRunsQuery q, CancellationToken ct = default) => _reader.ListAsync(q.ServiceId, q.MappingId, ct);
}

public sealed record GetRunByIdQuery(Guid Id);
public sealed class GetRunByIdHandler
{
    private readonly IRunReader _reader;
    public GetRunByIdHandler(IRunReader reader) => _reader = reader;
    public Task<DeploymentRunDto?> HandleAsync(GetRunByIdQuery q, CancellationToken ct = default) => _reader.GetByIdAsync(q.Id, ct);
}
