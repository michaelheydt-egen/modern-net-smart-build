using Microsoft.EntityFrameworkCore;

namespace Cicd.Web.Admin.Services.Builds;

/// <summary>
/// EF Core context for the local build-history mirror. One table, one composite
/// unique index — kept deliberately small so PR 2 stays additive.
/// </summary>
public sealed class BuildSyncDbContext : DbContext
{
    public DbSet<BuildRunRecord> BuildRuns => Set<BuildRunRecord>();

    public BuildSyncDbContext(DbContextOptions<BuildSyncDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var run = modelBuilder.Entity<BuildRunRecord>();

        // Uniqueness across (JobName, Number) — both the natural key for upserts
        // and what every list/detail query hits.
        run.HasIndex(r => new { r.JobName, r.Number }).IsUnique();

        // Used by the in-flight sweep: "find every Building row for job X to refresh".
        run.HasIndex(r => new { r.JobName, r.Building });

        run.Property(r => r.JobName).HasMaxLength(256).IsRequired();
        run.Property(r => r.Result).HasMaxLength(32);
    }
}
