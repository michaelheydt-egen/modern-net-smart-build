using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Deployment.Domain.Containers;
using Deployment.Domain.Environments;
using Deployment.Domain.Mappings;
using Deployment.Domain.Runs;
using Deployment.Domain.Services;

namespace Deployment.Infrastructure.Persistence.Configurations;

internal static class Json
{
    private static readonly JsonSerializerOptions Opts = new(JsonSerializerDefaults.Web);

    public static ValueConverter<IReadOnlyList<T>, string> Converter<T>() => new(
        v => JsonSerializer.Serialize(v, Opts),
        s => JsonSerializer.Deserialize<List<T>>(s, Opts) ?? new List<T>());

    public static ValueComparer<IReadOnlyList<T>> Comparer<T>() => new(
        (a, b) => JsonSerializer.Serialize(a, Opts) == JsonSerializer.Serialize(b, Opts),
        v => JsonSerializer.Serialize(v, Opts).GetHashCode(),
        v => v.ToList());
}

public sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> b)
    {
        b.ToTable("Service");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.Name).HasMaxLength(200).IsRequired();
        b.Property(s => s.ContainerName).HasMaxLength(300).IsRequired();
        b.Property(s => s.IsActive).IsRequired();
        b.Property(s => s.CreatedAtUtc).IsRequired();
        b.Property(s => s.UpdatedAtUtc).IsRequired();
        b.HasIndex(s => s.Name).IsUnique();
        b.HasIndex(s => s.ContainerName);
    }
}

public sealed class EnvironmentConfiguration : IEntityTypeConfiguration<DeploymentEnvironment>
{
    public void Configure(EntityTypeBuilder<DeploymentEnvironment> b)
    {
        b.ToTable("DeploymentEnvironment");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Name).HasMaxLength(200).IsRequired();
        b.Property(e => e.GcpProject).HasMaxLength(200).IsRequired();
        b.Property(e => e.Region).HasMaxLength(100).IsRequired();
        b.Property(e => e.GarRepository).HasMaxLength(200).IsRequired();
        b.Property(e => e.IsActive).IsRequired();
        b.Property(e => e.CreatedAtUtc).IsRequired();
        b.Property(e => e.UpdatedAtUtc).IsRequired();
        b.HasIndex(e => e.Name).IsUnique();
    }
}

public sealed class MappingConfiguration : IEntityTypeConfiguration<DeploymentMapping>
{
    public void Configure(EntityTypeBuilder<DeploymentMapping> b)
    {
        b.ToTable("DeploymentMapping");
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).ValueGeneratedNever();
        b.Property(m => m.ServiceId).IsRequired();
        b.Property(m => m.EnvironmentId).IsRequired();
        b.Property(m => m.CloudRunServiceName).HasMaxLength(300).IsRequired();
        b.Property(m => m.AutoDeploy).IsRequired();
        b.Property(m => m.CreatedAtUtc).IsRequired();
        b.Property(m => m.UpdatedAtUtc).IsRequired();

        b.Property(m => m.Steps)
            .HasConversion(Json.Converter<DeploymentStep>(), Json.Comparer<DeploymentStep>())
            .HasColumnType("nvarchar(max)");

        b.HasIndex(m => new { m.ServiceId, m.EnvironmentId }).IsUnique();
        b.HasIndex(m => m.EnvironmentId);
    }
}

public sealed class KnownContainerConfiguration : IEntityTypeConfiguration<KnownContainer>
{
    public void Configure(EntityTypeBuilder<KnownContainer> b)
    {
        b.ToTable("KnownContainer");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.ContainerName).HasMaxLength(300).IsRequired();
        b.Property(c => c.Version).HasMaxLength(200).IsRequired();
        b.Property(c => c.ImageDigest).HasMaxLength(200);
        b.Property(c => c.NexusRef).HasMaxLength(1000).IsRequired();
        b.Property(c => c.FirstSeenAtUtc).IsRequired();
        b.Property(c => c.LastSeenAtUtc).IsRequired();
        b.HasIndex(c => c.ContainerName).IsUnique();
    }
}

public sealed class RunConfiguration : IEntityTypeConfiguration<DeploymentRun>
{
    public void Configure(EntityTypeBuilder<DeploymentRun> b)
    {
        b.ToTable("DeploymentRun");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.MappingId).IsRequired();
        b.Property(r => r.ServiceId).IsRequired();
        b.Property(r => r.EnvironmentId).IsRequired();
        b.Property(r => r.ServiceName).HasMaxLength(200).IsRequired();
        b.Property(r => r.ContainerName).HasMaxLength(300).IsRequired();
        b.Property(r => r.Version).HasMaxLength(200).IsRequired();
        b.Property(r => r.SourceRef).HasMaxLength(1000).IsRequired();
        b.Property(r => r.GcpProject).HasMaxLength(200).IsRequired();
        b.Property(r => r.Region).HasMaxLength(100).IsRequired();
        b.Property(r => r.GarRepository).HasMaxLength(200).IsRequired();
        b.Property(r => r.CloudRunServiceName).HasMaxLength(300).IsRequired();
        b.Property(r => r.Trigger).HasConversion<int>().IsRequired();
        b.Property(r => r.TriggeredBy).HasMaxLength(200).IsRequired();
        b.Property(r => r.Status).HasConversion<int>().IsRequired();
        b.Property(r => r.RemoteImageRef).HasMaxLength(1000);
        b.Property(r => r.CloudRunRevision).HasMaxLength(300);
        b.Property(r => r.FailureReason).HasMaxLength(2000);
        b.Property(r => r.RequestedAtUtc).IsRequired();
        b.Property(r => r.CompletedAtUtc);

        b.Property(r => r.Steps)
            .HasConversion(Json.Converter<RunStepResult>(), Json.Comparer<RunStepResult>())
            .HasColumnType("nvarchar(max)");

        b.HasIndex(r => r.ServiceId);
        b.HasIndex(r => r.MappingId);
        b.HasIndex(r => r.Status);
    }
}
