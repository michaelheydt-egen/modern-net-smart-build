using Deployment.Contracts.Catalog;

namespace Deployment.Application.Features.Environments;

public interface IEnvironmentReader
{
    Task<IReadOnlyList<EnvironmentDto>> ListAsync(CancellationToken ct = default);
    Task<EnvironmentDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
}

public sealed record ListEnvironmentsQuery;
public sealed class ListEnvironmentsHandler
{
    private readonly IEnvironmentReader _reader;
    public ListEnvironmentsHandler(IEnvironmentReader reader) => _reader = reader;
    public Task<IReadOnlyList<EnvironmentDto>> HandleAsync(ListEnvironmentsQuery q, CancellationToken ct = default) => _reader.ListAsync(ct);
}

public sealed record GetEnvironmentByIdQuery(Guid Id);
public sealed class GetEnvironmentByIdHandler
{
    private readonly IEnvironmentReader _reader;
    public GetEnvironmentByIdHandler(IEnvironmentReader reader) => _reader = reader;
    public Task<EnvironmentDto?> HandleAsync(GetEnvironmentByIdQuery q, CancellationToken ct = default) => _reader.GetByIdAsync(q.Id, ct);
}
