namespace Jenkins.Contracts.Repositories;

public enum RepositoryProviderDto
{
    GitHub = 0,
    AzureDevOps = 1,
    GitLab = 2,
    Bitbucket = 3,
    Other = 4,
}

public enum BuildKindDto
{
    Standard = 0,
    Aspire = 1,
}

// --- Read-side DTOs ---

public sealed record RepositoryDto(
    Guid Id,
    string Name,
    string GitUrl,
    RepositoryProviderDto Provider,
    string DefaultBranch,
    string CiJobName,
    string BaseVersion,
    bool IsActive,
    bool AllowContainerPublish,
    BuildKindDto BuildKind,
    string? AppHostPath,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<DeployableComponentDto> Components);

public sealed record DeployableComponentDto(
    Guid Id,
    string ContainerName,
    Guid DeployableUnitId,
    string DeployableUnitName,
    bool AutoPublish,
    bool IsActive);

// --- Write-side requests ---

public sealed record RegisterRepositoryRequest(
    string Name,
    string GitUrl,
    RepositoryProviderDto Provider,
    string DefaultBranch,
    string CiJobName,
    string BaseVersion,
    BuildKindDto BuildKind = BuildKindDto.Standard,
    string? AppHostPath = null);

public sealed record UpdateRepositoryRequest(
    string Name,
    string GitUrl,
    RepositoryProviderDto Provider,
    string DefaultBranch,
    string CiJobName,
    string BaseVersion,
    BuildKindDto BuildKind = BuildKindDto.Standard,
    string? AppHostPath = null);

public sealed record SetRepositoryActiveRequest(bool IsActive);

public sealed record SetRepositoryContainerPublishRequest(bool AllowContainerPublish);

public sealed record MapComponentRequest(
    string ContainerName,
    Guid DeployableUnitId,
    string DeployableUnitName,
    bool AutoPublish);
