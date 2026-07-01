using Jenkins.Contracts.Repositories;
using Jenkins.Domain.SourceRepositories;

namespace Jenkins.Application.Features.Repositories;

/// <summary>
/// Single place that projects the domain <see cref="SourceRepository"/> to the
/// wire <see cref="RepositoryDto"/> and converts the provider enum.
/// </summary>
internal static class RepositoryMapping
{
    public static RepositoryDto ToDto(this SourceRepository r) => new(
        Id: r.Id,
        Name: r.Name,
        GitUrl: r.GitUrl,
        Provider: (RepositoryProviderDto)(int)r.Provider,
        DefaultBranch: r.DefaultBranch,
        CiJobName: r.CiJobName,
        BaseVersion: r.BaseVersion,
        IsActive: r.IsActive,
        AllowContainerPublish: r.AllowContainerPublish,
        BuildKind: (BuildKindDto)(int)r.BuildKind,
        AppHostPath: r.AppHostPath,
        CreatedAtUtc: r.CreatedAtUtc,
        Components: r.Components.OrderBy(c => c.ContainerName).Select(c => c.ToDto()).ToList());

    public static DeployableComponentDto ToDto(this DeployableComponent c) => new(
        Id: c.Id,
        ContainerName: c.ContainerName,
        DeployableUnitId: c.DeployableUnitId,
        DeployableUnitName: c.DeployableUnitName,
        AutoPublish: c.AutoPublish,
        IsActive: c.IsActive);

    public static RepositoryProvider ToDomain(this RepositoryProviderDto p) => (RepositoryProvider)(int)p;
    public static BuildKind ToDomain(this BuildKindDto k) => (BuildKind)(int)k;
}
