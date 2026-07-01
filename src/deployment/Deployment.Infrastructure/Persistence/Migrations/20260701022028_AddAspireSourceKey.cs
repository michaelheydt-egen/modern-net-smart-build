using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deployment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAspireSourceKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceKey",
                table: "AspireApplication",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspireApplication_SourceKey",
                table: "AspireApplication",
                column: "SourceKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspireApplication_SourceKey",
                table: "AspireApplication");

            migrationBuilder.DropColumn(
                name: "SourceKey",
                table: "AspireApplication");
        }
    }
}
