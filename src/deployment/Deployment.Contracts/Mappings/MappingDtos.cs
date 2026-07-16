namespace Deployment.Contracts.Mappings;

public enum DeploymentStepKindDto
{
    GarPush = 0,
    CloudRunDeploy = 1,
    KubernetesApply = 2,
}

public sealed record DeploymentStepDto(int Order, DeploymentStepKindDto Kind);

public enum RolloutStrategyDto { Direct = 0, BlueGreen = 1, Canary = 2 }
public enum PromotionModeDto { Automatic = 0, Manual = 1 }

public sealed record KubernetesSpecDto(
    string DeploymentName, int ContainerPort, int Replicas,
    IReadOnlyDictionary<string, string>? EnvVars, string? ImagePullSecret, bool CreateService,
    RolloutStrategyDto Strategy = RolloutStrategyDto.Direct, PromotionModeDto PromotionMode = PromotionModeDto.Automatic,
    int CanaryWeightPercent = 20,
    IReadOnlyList<int>? CanarySteps = null);

public sealed record DeploymentMappingDto(
    Guid Id,
    Guid ServiceId,
    string ServiceName,
    Guid EnvironmentId,
    string EnvironmentName,
    string? CloudRunServiceName,
    KubernetesSpecDto? Kubernetes,
    bool AutoDeploy,
    IReadOnlyList<DeploymentStepDto> Steps,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>Provide a CloudRunServiceName (Cloud Run env) or a Kubernetes spec (Kubernetes env). Steps optional — defaulted by target.</summary>
public sealed record CreateMappingRequest(
    Guid ServiceId, Guid EnvironmentId, string? CloudRunServiceName, KubernetesSpecDto? Kubernetes, bool AutoDeploy, IReadOnlyList<DeploymentStepDto>? Steps);

public sealed record UpdateMappingRequest(
    string? CloudRunServiceName, KubernetesSpecDto? Kubernetes, IReadOnlyList<DeploymentStepDto>? Steps);

public sealed record SetAutoDeployRequest(bool AutoDeploy);
