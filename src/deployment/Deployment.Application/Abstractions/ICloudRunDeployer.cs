namespace Deployment.Application.Abstractions;

/// <summary>Inputs for a Cloud Run deploy: which service in which project/region, and the image.</summary>
public sealed record CloudRunDeployRequest(string Project, string Region, string ServiceName, string Image);

/// <summary>
/// Deploys a container image to a Google Cloud Run service (create-or-update the service's
/// revision template to the image, route 100% traffic, wait for Ready). Returns the ready
/// revision name. Auth via ADC. Infrastructure-backed by the Google.Cloud.Run.V2 client.
/// </summary>
public interface ICloudRunDeployer
{
    Task<string> DeployAsync(CloudRunDeployRequest request, CancellationToken cancellationToken = default);
}
