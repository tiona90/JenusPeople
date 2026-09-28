using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <summary>
    /// Data only: HR Administrators stopped being eligible assignees (they run tasks,
    /// they are never handed one), so this takes them off the tasks they were already
    /// on. Only where somebody else is still assigned — a task whose sole assignee is
    /// an HR Administrator has nobody to fall back to, so it keeps them, and the next
    /// edit refuses to save until someone eligible is picked
    /// (<c>WorkTaskAssigneeRule.HrNotAssignableMessage</c>). Down restores nothing: the
    /// removed rows are not recorded anywhere to put back.
    /// </summary>
    public partial class RemoveHrAdministratorsFromTasks : Migration
    {
        private const string HrAssignees =
            "SELECT ur.UserId FROM AspNetUserRoles ur " +
            "JOIN AspNetRoles r ON r.Id = ur.RoleId WHERE r.Name = 'HR Administrator'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE a FROM WorkTaskAssignees a " +
                $"WHERE a.UserId IN ({HrAssignees}) " +
                "AND EXISTS (SELECT 1 FROM WorkTaskAssignees o " +
                "WHERE o.WorkTaskId = a.WorkTaskId " +
                $"AND o.UserId NOT IN ({HrAssignees}));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
