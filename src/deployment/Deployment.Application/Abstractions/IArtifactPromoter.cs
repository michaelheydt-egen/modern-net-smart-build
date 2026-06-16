namespace Deployment.Application.Abstractions;

/// <summary>
/// Copies a container image digest-preserving from one registry to another (Nexus → GAR). The
/// infrastructure default shells out to <c>crane copy</c>. Idempotent: re-copying an existing
/// digest is a no-op.
/// </summary>
public interface IArtifactPromoter
{
    Task EnsureCopiedAsync(string sourceRef, string destinationRef, CancellationToken cancellationToken = default);
}
