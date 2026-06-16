using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deployment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeploymentEnvironment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    GcpProject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GarRepository = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentEnvironment", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentMapping",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CloudRunServiceName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    AutoDeploy = table.Column<bool>(type: "bit", nullable: false),
                    Steps = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentMapping", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentRun",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MappingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContainerName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SourceRef = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    GcpProject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GarRepository = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CloudRunServiceName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Trigger = table.Column<int>(type: "int", nullable: false),
                    TriggeredBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RemoteImageRef = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CloudRunRevision = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Steps = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentRun", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KnownContainer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContainerName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ImageDigest = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NexusRef = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    FirstSeenAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnownContainer", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Service",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContainerName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Service", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentEnvironment_Name",
                table: "DeploymentEnvironment",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentMapping_EnvironmentId",
                table: "DeploymentMapping",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentMapping_ServiceId_EnvironmentId",
                table: "DeploymentMapping",
                columns: new[] { "ServiceId", "EnvironmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentRun_MappingId",
                table: "DeploymentRun",
                column: "MappingId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentRun_ServiceId",
                table: "DeploymentRun",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentRun_Status",
                table: "DeploymentRun",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_KnownContainer_ContainerName",
                table: "KnownContainer",
                column: "ContainerName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Service_ContainerName",
                table: "Service",
                column: "ContainerName");

            migrationBuilder.CreateIndex(
                name: "IX_Service_Name",
                table: "Service",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeploymentEnvironment");

            migrationBuilder.DropTable(
                name: "DeploymentMapping");

            migrationBuilder.DropTable(
                name: "DeploymentRun");

            migrationBuilder.DropTable(
                name: "KnownContainer");

            migrationBuilder.DropTable(
                name: "Service");
        }
    }
}
