using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectComponentAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectComponentAssignments",
                columns: table => new
                {
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    ComponentId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectComponentAssignments", x => new { x.ProjectId, x.ComponentId });
                    table.ForeignKey(
                        name: "FK_ProjectComponentAssignments_ProjectComponents_ComponentId",
                        column: x => x.ComponentId,
                        principalTable: "ProjectComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectComponentAssignments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectComponentAssignments_ComponentId",
                table: "ProjectComponentAssignments",
                column: "ComponentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectComponentAssignments");
        }
    }
}
