namespace Deployment.Contracts.Mappings;

public enum DeploymentStepKindDto
{
    GarPush = 0,
    CloudRunDeploy = 1,
}

public sealed record DeploymentStepDto(int Order, DeploymentStepKindDto Kind);

public sealed record DeploymentMappingDto(
    Guid Id,
    Guid ServiceId,
    string ServiceName,
    Guid EnvironmentId,
    string EnvironmentName,
    string CloudRunServiceName,
    bool AutoDeploy,
    IReadOnlyList<DeploymentStepDto> Steps,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>Steps optional — when omitted the default GarPush→CloudRunDeploy recipe is used.</summary>
public sealed record CreateMappingRequest(
    Guid ServiceId, Guid EnvironmentId, string CloudRunServiceName, bool AutoDeploy, IReadOnlyList<DeploymentStepDto>? Steps);

public sealed record UpdateMappingRequest(
    string CloudRunServiceName, IReadOnlyList<DeploymentStepDto>? Steps);

public sealed record SetAutoDeployRequest(bool AutoDeploy);
