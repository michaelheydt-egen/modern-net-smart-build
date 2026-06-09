using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cicd.Web.Admin.Services.Builds;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> discover <see cref="BuildSyncDbContext"/>
/// without running the full host. The path here is only used to generate
/// migration SQL — the runtime path comes from <see cref="BuildSyncOptions.DbPath"/>.
/// </summary>
public sealed class BuildSyncDesignTimeFactory : IDesignTimeDbContextFactory<BuildSyncDbContext>
{
    public BuildSyncDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BuildSyncDbContext>()
            .UseSqlite("Data Source=./data/builds.db")
            .Options;
        return new BuildSyncDbContext(options);
    }
}
