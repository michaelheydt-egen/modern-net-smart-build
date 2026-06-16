namespace Deployment.Contracts.Catalog;

public sealed record ServiceDto(
    Guid Id, string Name, string ContainerName, bool IsActive,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record CreateServiceRequest(string Name, string ContainerName);
public sealed record UpdateServiceRequest(string Name, string ContainerName);

public sealed record EnvironmentDto(
    Guid Id, string Name, string GcpProject, string Region, string GarRepository, bool IsActive,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record CreateEnvironmentRequest(string Name, string GcpProject, string Region, string GarRepository);
public sealed record UpdateEnvironmentRequest(string Name, string GcpProject, string Region, string GarRepository);
