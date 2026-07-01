using Jenkins.Domain.SourceRepositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jenkins.Infrastructure.Persistence.Configurations.SourceRepositories;

/// <summary>
/// The tracked-repo aggregate. Name is unique across the catalog; the
/// <see cref="DeployableComponent"/> mappings are an owned child collection within
/// the aggregate boundary (cascade-deleted with the repo). Enums stored as ints.
/// </summary>
public sealed class SourceRepositoryConfiguration : IEntityTypeConfiguration<SourceRepository>
{
    public void Configure(EntityTypeBuilder<SourceRepository> b)
    {
        b.ToTable("SourceRepository");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Name).HasMaxLength(200).IsRequired();
        b.Property(r => r.GitUrl).HasMaxLength(500).IsRequired();
        b.Property(r => r.Provider).HasConversion<int>().IsRequired();
        b.Property(r => r.DefaultBranch).HasMaxLength(200).IsRequired();
        b.Property(r => r.CiJobName).HasMaxLength(200).IsRequired();
        b.Property(r => r.BaseVersion).HasMaxLength(64).IsRequired();
        b.Property(r => r.IsActive).IsRequired();
        // Default true so existing rows keep producing containers after the column is added.
        b.Property(r => r.AllowContainerPublish).IsRequired().HasDefaultValue(true);
        b.Property(r => r.BuildKind).HasConversion<int>().IsRequired().HasDefaultValue(BuildKind.Standard);
        b.Property(r => r.AppHostPath).HasMaxLength(500);
        b.Property(r => r.CreatedAtUtc).IsRequired();
        b.HasIndex(r => r.Name).IsUnique();

        b.HasMany(r => r.Components)
            .WithOne()
            .HasForeignKey(c => c.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(r => r.Components).AutoInclude();
    }
}

public sealed class DeployableComponentConfiguration : IEntityTypeConfiguration<DeployableComponent>
{
    public void Configure(EntityTypeBuilder<DeployableComponent> b)
    {
        b.ToTable("DeployableComponent");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.RepositoryId).IsRequired();
        b.Property(c => c.ContainerName).HasMaxLength(200).IsRequired();
        b.Property(c => c.DeployableUnitId).IsRequired();
        b.Property(c => c.DeployableUnitName).HasMaxLength(200).IsRequired();
        b.Property(c => c.AutoPublish).IsRequired();
        b.Property(c => c.IsActive).IsRequired();

        // Container names are unique within a repo (decisions §9).
        b.HasIndex(c => new { c.RepositoryId, c.ContainerName }).IsUnique();
    }
}
