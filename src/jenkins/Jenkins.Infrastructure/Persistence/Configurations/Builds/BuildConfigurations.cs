using System.Text.Json;
using Jenkins.Domain.Builds;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jenkins.Infrastructure.Persistence.Configurations.Builds;

/// <summary>
/// The build aggregate. Value objects (SourceRevision, Versions, Quality) are
/// stored as columns on the Build row via <c>OwnsOne</c>; artifacts and their
/// publications are child collections within the aggregate boundary.
/// </summary>
public sealed class BuildConfiguration : IEntityTypeConfiguration<Build>
{
    public void Configure(EntityTypeBuilder<Build> b)
    {
        b.ToTable("Build");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.RepositoryId).IsRequired();
        b.Property(x => x.CiJobName).HasMaxLength(200).IsRequired();
        b.Property(x => x.CiBuildNumber).IsRequired();
        b.Property(x => x.CiRunUrl).HasMaxLength(500).IsRequired();
        b.Property(x => x.CiRunId).HasMaxLength(200).IsRequired();
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.StartedAtUtc).IsRequired();
        b.Property(x => x.CompletedAtUtc);
        b.Property(x => x.DurationMs);
        b.Property(x => x.TriggeredBy).HasMaxLength(200);
        b.Property(x => x.AspireManifestUrl).HasMaxLength(500);

        b.Ignore(x => x.IsTerminal);

        // Required VO — the commit a build was produced from.
        b.OwnsOne(x => x.SourceRevision, sr =>
        {
            sr.Property(p => p.CommitSha).HasColumnName("CommitSha").HasMaxLength(64).IsRequired();
            sr.Property(p => p.CommitShort).HasColumnName("CommitShort").HasMaxLength(40).IsRequired();
            sr.Property(p => p.Branch).HasColumnName("Branch").HasMaxLength(200).IsRequired();
            sr.Property(p => p.Author).HasColumnName("CommitAuthor").HasMaxLength(200);
            sr.Property(p => p.Message).HasColumnName("CommitMessage").HasMaxLength(2000);
            sr.Property(p => p.CommittedAtUtc).HasColumnName("CommittedAtUtc");
        });
        b.Navigation(x => x.SourceRevision).IsRequired();

        // Optional VO — the resolved version block (set on success). PackageVersion
        // is the identifying property (present ⇒ the VO is present).
        b.OwnsOne(x => x.Versions, v =>
        {
            v.Property(p => p.PackageVersion).HasColumnName("PackageVersion").HasMaxLength(200).IsRequired();
            v.Property(p => p.FileVersion).HasColumnName("FileVersion").HasMaxLength(100).IsRequired();
            v.Property(p => p.AssemblyVersion).HasColumnName("AssemblyVersion").HasMaxLength(100).IsRequired();
            v.Property(p => p.InformationalVersion).HasColumnName("InformationalVersion").HasMaxLength(300).IsRequired();
            v.Property(p => p.BaseVersion).HasColumnName("BaseVersion").HasMaxLength(64).IsRequired();
        });

        // Optional VO — supply-chain outputs (SBOM + vuln report URIs).
        b.OwnsOne(x => x.Quality, q =>
        {
            q.Property(p => p.SbomUri).HasColumnName("SbomUri").HasMaxLength(500).IsRequired();
            q.Property(p => p.VulnerabilityReportUri).HasColumnName("VulnerabilityReportUri").HasMaxLength(500).IsRequired();
        });

        b.HasMany(x => x.Artifacts)
            .WithOne()
            .HasForeignKey(a => a.BuildId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Artifacts).AutoInclude();

        b.HasIndex(x => new { x.CiJobName, x.CiBuildNumber }).IsUnique();
        b.HasIndex(x => new { x.RepositoryId, x.StartedAtUtc });
    }
}

public sealed class BuildArtifactConfiguration : IEntityTypeConfiguration<BuildArtifact>
{
    public void Configure(EntityTypeBuilder<BuildArtifact> b)
    {
        b.ToTable("BuildArtifact");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.BuildId).IsRequired();
        b.Property(x => x.Kind).HasConversion<int>().IsRequired();
        b.Property(x => x.Name).HasMaxLength(300).IsRequired();
        b.Property(x => x.Version).HasMaxLength(200).IsRequired();
        b.Property(x => x.Digest).HasMaxLength(200).IsRequired();
        b.Property(x => x.SizeBytes);
        b.Property(x => x.ProducedAtUtc).IsRequired();

        b.Ignore(x => x.IsContainerImage);

        b.HasMany(x => x.Publications)
            .WithOne()
            .HasForeignKey(p => p.BuildArtifactId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Publications).AutoInclude();

        b.HasIndex(x => x.BuildId);
    }
}

public sealed class ArtifactPublicationConfiguration : IEntityTypeConfiguration<ArtifactPublication>
{
    public void Configure(EntityTypeBuilder<ArtifactPublication> b)
    {
        b.ToTable("ArtifactPublication");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.BuildArtifactId).IsRequired();
        b.Property(x => x.Registry).HasConversion<int>().IsRequired();
        b.Property(x => x.Reference).HasMaxLength(500).IsRequired();
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.PublishedAtUtc).IsRequired();

        // Tags: store the backing field as a JSON text column. Ignore the
        // read-only projection property so EF doesn't also try to map it.
        b.Ignore(x => x.Tags);
        var tagsComparer = new ValueComparer<List<string>>(
            (a, c) => (a ?? new List<string>()).SequenceEqual(c ?? new List<string>()),
            v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
            v => v.ToList());
        b.Property<List<string>>("_tags")
            .HasColumnName("Tags")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>(),
                tagsComparer);

        b.HasIndex(x => x.BuildArtifactId);
    }
}
