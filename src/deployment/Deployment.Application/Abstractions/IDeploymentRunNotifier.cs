namespace Deployment.Application.Abstractions;

/// <summary>
/// Pushes a terminal deployment-run notification to all connected clients (SignalR), so the UI can
/// raise an app-wide completion toast. Implemented in the API host where the hub lives; the handler
/// depends only on this port.
/// </summary>
public interface IDeploymentRunNotifier
{
    Task RunCompletedAsync(Guid runId, string status, string title, string? detail, CancellationToken cancellationToken = default);
}
