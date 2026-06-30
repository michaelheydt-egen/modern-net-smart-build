using Microsoft.AspNetCore.SignalR;

namespace Deployment.Api.Hubs;

/// <summary>
/// Completion stream for deployment runs. Bare hub — clients just connect and listen for the global
/// <c>DeploymentCompleted</c> broadcast pushed by <see cref="DeploymentRunNotifier"/> when a run
/// settles (no per-run subscription needed).
/// </summary>
public sealed class DeploymentRunHub : Hub
{
}
