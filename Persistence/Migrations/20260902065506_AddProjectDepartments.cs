using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <summary>
    /// Moves a project's single optional department onto a join table, so a project
    /// can belong to several — and so those departments can decide who sees it.
    ///
    /// The order below is deliberate and differs from what EF scaffolds. The table
    /// is created and filled from the old column before that column is dropped;
    /// scaffolding drops it first, which would leave every existing project with no
    /// department and therefore visible to nobody but an admin.
    /// </summary>
    public partial class AddProjectDepartments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectDepartments",
                columns: table => new
                {
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    DepartmentId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectDepartments", x => new { x.ProjectId, x.DepartmentId });
                    table.ForeignKey(
                        name: "FK_ProjectDepartments_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectDepartments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDepartments_DepartmentId",
                table: "ProjectDepartments",
                column: "DepartmentId");

            // Carry each project's existing department across. A project that had
            // none keeps none, and stays admin-only until someone assigns one.
            migrationBuilder.Sql(@"
                INSERT INTO ProjectDepartments (ProjectId, DepartmentId)
                SELECT Id, DepartmentId FROM Projects WHERE DepartmentId IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_Projects_Departments_DepartmentId",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Projects_DepartmentId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "Projects");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DepartmentId",
                table: "Projects",
                type: "int",
                nullable: true);

            // The old column holds one department, so a project assigned several
            // keeps only the lowest-numbered one. Lossy by nature — there is
            // nowhere else for the rest to go.
            migrationBuilder.Sql(@"
                UPDATE Projects
                SET DepartmentId = (
                    SELECT MIN(pd.DepartmentId) FROM ProjectDepartments pd
                    WHERE pd.ProjectId = Projects.Id);");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_DepartmentId",
                table: "Projects",
                column: "DepartmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_Departments_DepartmentId",
                table: "Projects",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropTable(
                name: "ProjectDepartments");
        }
    }
}
