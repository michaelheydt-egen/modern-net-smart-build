using Jenkins.Domain.Abstractions;
using Jenkins.Domain.Builds;
using FluentValidation;

namespace Jenkins.Application.Features.Builds;

/// <summary>
/// Record that an Aspire build published its Kustomize-manifest archive to Nexus. Idempotent on
/// the manifest URL (the domain no-ops a repeat), so the sync worker can call it each tick.
/// Raises the <c>AspireManifestPublished</c> domain event → integration event for the CI→deploy handoff.
/// </summary>
public sealed record RecordAspireManifestCommand(
    Guid BuildId,
    string AppName,
    string ManifestUrl,
    string Version);

public sealed class RecordAspireManifestValidator : AbstractValidator<RecordAspireManifestCommand>
{
    public RecordAspireManifestValidator()
    {
        RuleFor(x => x.BuildId).NotEmpty();
        RuleFor(x => x.AppName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ManifestUrl).NotEmpty().MaximumLength(500);
    }
}

public sealed class RecordAspireManifestHandler
{
    private readonly IBuildStore _builds;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public RecordAspireManifestHandler(IBuildStore builds, IUnitOfWork uow, TimeProvider clock)
    {
        _builds = builds;
        _uow = uow;
        _clock = clock;
    }

    public async Task HandleAsync(RecordAspireManifestCommand cmd, CancellationToken cancellationToken = default)
    {
        var build = await _builds.GetByIdAsync(cmd.BuildId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Build {cmd.BuildId} not found.");

        build.RecordAspireManifest(cmd.AppName, cmd.ManifestUrl, cmd.Version, _clock.GetUtcNow());

        await _uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
