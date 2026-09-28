using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <summary>
    /// A task goes from one assignee to several. Hand-ordered, not as generated: the
    /// generator dropped <c>WorkTasks.AssigneeId</c> before creating the table meant
    /// to hold it, which would have left every existing task with nobody on it. Up
    /// creates <c>WorkTaskAssignees</c>, copies each task's assignee into it, and only
    /// then drops the column; Down puts one assignee back per task the same way round.
    /// </summary>
    public partial class AllowSeveralTaskAssignees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkTaskAssignees",
                columns: table => new
                {
                    WorkTaskId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTaskAssignees", x => new { x.WorkTaskId, x.UserId });
                    table.ForeignKey(
                        name: "FK_WorkTaskAssignees_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkTaskAssignees_WorkTasks_WorkTaskId",
                        column: x => x.WorkTaskId,
                        principalTable: "WorkTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskAssignees_UserId",
                table: "WorkTaskAssignees",
                column: "UserId");

            // Every existing task keeps the one person it was given.
            migrationBuilder.Sql(
                "INSERT INTO WorkTaskAssignees (WorkTaskId, UserId) " +
                "SELECT Id, AssigneeId FROM WorkTasks WHERE AssigneeId IS NOT NULL AND AssigneeId <> '';");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkTasks_AspNetUsers_AssigneeId",
                table: "WorkTasks");

            migrationBuilder.DropIndex(
                name: "IX_WorkTasks_AssigneeId",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "AssigneeId",
                table: "WorkTasks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssigneeId",
                table: "WorkTasks",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                defaultValue: "");

            // One column holds one person: keep the first assignee by id, or the
            // creator for a task that somehow has none, so the foreign key below holds.
            migrationBuilder.Sql(
                "UPDATE t SET AssigneeId = COALESCE(" +
                "(SELECT MIN(a.UserId) FROM WorkTaskAssignees a WHERE a.WorkTaskId = t.Id), t.CreatedById) " +
                "FROM WorkTasks t;");

            migrationBuilder.DropTable(
                name: "WorkTaskAssignees");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_AssigneeId",
                table: "WorkTasks",
                column: "AssigneeId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkTasks_AspNetUsers_AssigneeId",
                table: "WorkTasks",
                column: "AssigneeId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
