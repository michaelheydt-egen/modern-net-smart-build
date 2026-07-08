using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deployment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAspireRunDeployedImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeployedImages",
                table: "AspireApplicationRun",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeployedImages",
                table: "AspireApplicationRun");
        }
    }
}
