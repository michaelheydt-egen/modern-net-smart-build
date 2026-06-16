using Deployment.Contracts.Catalog;

namespace Deployment.Application.Features.Services;

public interface IServiceReader
{
    Task<IReadOnlyList<ServiceDto>> ListAsync(CancellationToken ct = default);
    Task<ServiceDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
}

public sealed record ListServicesQuery;
public sealed class ListServicesHandler
{
    private readonly IServiceReader _reader;
    public ListServicesHandler(IServiceReader reader) => _reader = reader;
    public Task<IReadOnlyList<ServiceDto>> HandleAsync(ListServicesQuery q, CancellationToken ct = default) => _reader.ListAsync(ct);
}

public sealed record GetServiceByIdQuery(Guid Id);
public sealed class GetServiceByIdHandler
{
    private readonly IServiceReader _reader;
    public GetServiceByIdHandler(IServiceReader reader) => _reader = reader;
    public Task<ServiceDto?> HandleAsync(GetServiceByIdQuery q, CancellationToken ct = default) => _reader.GetByIdAsync(q.Id, ct);
}
