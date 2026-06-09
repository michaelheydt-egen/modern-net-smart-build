using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cicd.Web.Admin.Services.Builds.Migrations
{
    /// <inheritdoc />
    public partial class InitialBuildSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BuildRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Result = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Building = table.Column<bool>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<long>(type: "INTEGER", nullable: false),
                    Duration = table.Column<long>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    CausesJson = table.Column<string>(type: "TEXT", nullable: true),
                    ArtifactsJson = table.Column<string>(type: "TEXT", nullable: true),
                    BuildInfoJson = table.Column<string>(type: "TEXT", nullable: true),
                    SyncedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuildRuns_JobName_Building",
                table: "BuildRuns",
                columns: new[] { "JobName", "Building" });

            migrationBuilder.CreateIndex(
                name: "IX_BuildRuns_JobName_Number",
                table: "BuildRuns",
                columns: new[] { "JobName", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuildRuns");
        }
    }
}
