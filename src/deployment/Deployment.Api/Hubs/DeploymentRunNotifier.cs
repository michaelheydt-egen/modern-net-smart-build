using Deployment.Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace Deployment.Api.Hubs;

/// <summary>
/// <see cref="IDeploymentRunNotifier"/> over the SignalR <see cref="DeploymentRunHub"/> — broadcasts
/// a run's terminal outcome to all connected clients for the app-wide completion toast.
/// </summary>
internal sealed class DeploymentRunNotifier : IDeploymentRunNotifier
{
    private readonly IHubContext<DeploymentRunHub> _hub;

    public DeploymentRunNotifier(IHubContext<DeploymentRunHub> hub) => _hub = hub;

    public Task RunCompletedAsync(Guid runId, string status, string title, string? detail, CancellationToken cancellationToken = default)
        => _hub.Clients.All.SendAsync(
            "DeploymentCompleted",
            new { runId, status, title, detail },
            cancellationToken);
}
