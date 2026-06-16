namespace Deployment.Domain.Mappings;

/// <summary>
/// A typed step in a deployment recipe. Extensible — new target kinds (GKE, ACR, …) add a value
/// here plus an executor. For now: promote the image to GAR, then deploy to Cloud Run.
/// </summary>
public enum DeploymentStepKind
{
    GarPush = 0,
    CloudRunDeploy = 1,
}
