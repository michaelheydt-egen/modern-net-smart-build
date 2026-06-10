namespace Jenkins.Contracts.Repositories;

public enum RepositoryProviderDto
{
    GitHub = 0,
    AzureDevOps = 1,
    GitLab = 2,
    Bitbucket = 3,
    Other = 4,
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
    string BaseVersion);

public sealed record UpdateRepositoryRequest(
    string Name,
    string GitUrl,
    RepositoryProviderDto Provider,
    string DefaultBranch,
    string CiJobName,
    string BaseVersion);

public sealed record SetRepositoryActiveRequest(bool IsActive);

public sealed record MapComponentRequest(
    string ContainerName,
    Guid DeployableUnitId,
    string DeployableUnitName,
    bool AutoPublish);
