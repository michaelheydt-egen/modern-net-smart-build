using Jenkins.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Jenkins.Infrastructure.Releases;

/// <summary>
/// Inert <see cref="IDeploymentReleaseClient"/> — the deployment microservice was removed, so the
/// CI→deployment release handoff has nowhere to go. The auto-publish path still runs but this
/// client does nothing (logs once at debug). Replace with a real client if the deployment service
/// is reintroduced.
/// </summary>
internal sealed class NoOpDeploymentReleaseClient : IDeploymentReleaseClient
{
    private readonly ILogger<NoOpDeploymentReleaseClient> _logger;

    public NoOpDeploymentReleaseClient(ILogger<NoOpDeploymentReleaseClient> logger) => _logger = logger;

    public Task<Guid> PublishContainerReleaseAsync(PublishReleaseInput input, CancellationToken ct = default)
    {
        _logger.LogDebug("[handoff] deployment service not present — skipping release publish for {Unit} {Version}.",
            input.DeployableUnitId, input.SemanticVersion);
        return Task.FromResult(Guid.Empty);
    }

    public Task AttachProvenanceAsync(Guid releaseId, AttachProvenanceInput input, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<Guid?> GetReleaseIdByVersionAsync(Guid deployableUnitId, string semanticVersion, CancellationToken ct = default)
        => Task.FromResult<Guid?>(null);
}
