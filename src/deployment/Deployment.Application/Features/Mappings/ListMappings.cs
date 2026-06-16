using Deployment.Contracts.Mappings;

namespace Deployment.Application.Features.Mappings;

public interface IMappingReader
{
    Task<IReadOnlyList<DeploymentMappingDto>> ListAsync(Guid? serviceId, CancellationToken ct = default);
    Task<DeploymentMappingDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
}

public sealed record ListMappingsQuery(Guid? ServiceId);
public sealed class ListMappingsHandler
{
    private readonly IMappingReader _reader;
    public ListMappingsHandler(IMappingReader reader) => _reader = reader;
    public Task<IReadOnlyList<DeploymentMappingDto>> HandleAsync(ListMappingsQuery q, CancellationToken ct = default) => _reader.ListAsync(q.ServiceId, ct);
}

public sealed record GetMappingByIdQuery(Guid Id);
public sealed class GetMappingByIdHandler
{
    private readonly IMappingReader _reader;
    public GetMappingByIdHandler(IMappingReader reader) => _reader = reader;
    public Task<DeploymentMappingDto?> HandleAsync(GetMappingByIdQuery q, CancellationToken ct = default) => _reader.GetByIdAsync(q.Id, ct);
}
