using Jenkins.Contracts.Pipelines;
using Jenkins.Domain.Abstractions;
using Jenkins.Domain.Pipelines;

namespace Jenkins.Application.Features.Pipelines;

/// <summary>Read-model port for the pipeline list (flat summaries).</summary>
public interface IPipelineReader
{
    Task<IReadOnlyList<PipelineSummaryDto>> ListAsync(CancellationToken cancellationToken = default);
}

public sealed record ListPipelinesQuery;

public sealed class ListPipelinesHandler
{
    private readonly IPipelineReader _reader;
    public ListPipelinesHandler(IPipelineReader reader) => _reader = reader;

    public Task<IReadOnlyList<PipelineSummaryDto>> HandleAsync(ListPipelinesQuery query, CancellationToken cancellationToken = default)
        => _reader.ListAsync(cancellationToken);
}

public sealed record GetPipelineByIdQuery(Guid Id);

public sealed class GetPipelineByIdHandler
{
    private readonly IPipelineStore _pipelines;
    public GetPipelineByIdHandler(IPipelineStore pipelines) => _pipelines = pipelines;

    public async Task<PipelineDto?> HandleAsync(GetPipelineByIdQuery query, CancellationToken cancellationToken = default)
    {
        var pipeline = await _pipelines.GetByIdAsync(query.Id, cancellationToken).ConfigureAwait(false);
        return pipeline?.ToDto();
    }
}

/// <summary>
/// Seeds the built-in pipelines at host startup (idempotent): the default "CICD Main" chain (only on
/// a fresh database, to preserve the previously-hardcoded chain) and the "Aspire build" pipeline
/// (whenever it's absent, so existing installs pick it up too).
/// </summary>
public sealed class SeedDefaultPipelineHandler
{
    private const string CicdMainName = "CICD Main";
    private const string AspireName = "Aspire build";

    private readonly IPipelineStore _pipelines;
    private readonly IPipelineReader _reader;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public SeedDefaultPipelineHandler(IPipelineStore pipelines, IPipelineReader reader, IUnitOfWork uow, TimeProvider clock)
    {
        _pipelines = pipelines;
        _reader = reader;
        _uow = uow;
        _clock = clock;
    }

    public async Task HandleAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _reader.ListAsync(cancellationToken).ConfigureAwait(false);
        var names = existing.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = _clock.GetUtcNow();
        var added = 0;

        // Default chain — only on a fresh DB (keep in sync with DefaultPipelines.CicdMain()).
        if (existing.Count == 0)
        {
            var main = new Pipeline(Guid.NewGuid(), CicdMainName,
                "Build, scan (SBOM + vulnerabilities), then publish the NuGet package and container image to Nexus.", now);
            main.AddStage(Guid.NewGuid(), "cicd-build", null, null, now);
            main.AddStage(Guid.NewGuid(), "cicd-scan", "cicd-build", null, now);
            main.AddStage(Guid.NewGuid(), "cicd-publish-nexus-nuget", "cicd-scan", null, now);
            main.AddStage(Guid.NewGuid(), "cicd-publish-nexus-docker", "cicd-scan", null, now);
            await _pipelines.AddAsync(main, cancellationToken).ConfigureAwait(false);
            added++;
        }

        // Aspire build — ensure it exists (keep in sync with DefaultPipelines.CicdAspire()).
        if (!names.Contains(AspireName))
        {
            var aspire = new Pipeline(Guid.NewGuid(), AspireName,
                "Build a .NET Aspire app with Aspir8 and publish its images + Kustomize manifest artifact to Nexus.", now);
            aspire.AddStage(Guid.NewGuid(), "cicd-aspire-publish", null, null, now);
            await _pipelines.AddAsync(aspire, cancellationToken).ConfigureAwait(false);
            added++;
        }

        if (added > 0) await _uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
