namespace Deployment.Domain.AspireApps.Runs;

/// <summary>
/// A container image a successful run put on the cluster: the workload (Deployment) name and the image
/// reference it runs — digest-pinned when the deploy pinned it. Snapshotted at deploy time so the live-status
/// view can diff what's running now against what we deployed (image drift).
/// </summary>
public sealed record DeployedImage(string Workload, string Image);
