using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkTaskProjectType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProjectTypeId",
                table: "WorkTasks",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_ProjectTypeId",
                table: "WorkTasks",
                column: "ProjectTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkTasks_ProjectTypes_ProjectTypeId",
                table: "WorkTasks",
                column: "ProjectTypeId",
                principalTable: "ProjectTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkTasks_ProjectTypes_ProjectTypeId",
                table: "WorkTasks");

            migrationBuilder.DropIndex(
                name: "IX_WorkTasks_ProjectTypeId",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "ProjectTypeId",
                table: "WorkTasks");
        }
    }
}
