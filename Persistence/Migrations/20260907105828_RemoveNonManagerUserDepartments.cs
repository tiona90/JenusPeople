using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <summary>
    /// Data fix, no schema change. Deletes every <c>UserDepartments</c> row whose
    /// user is not in the Manager role.
    ///
    /// The row means one thing — an extra department a <b>manager</b> covers, on top
    /// of the one on their own profile — and nothing reads it for anyone else:
    /// <c>ProjectScope.DepartmentIdsForAsync</c> consults the table only when the
    /// caller is a manager, and an Admin short-circuits to "sees everything" before
    /// departments are resolved at all. One place did read it for everyone,
    /// <c>DeleteDepartment</c>, which counts each row as an "assigned manager"
    /// blocker. So a row granting nothing still made its department undeletable, and
    /// unblockable: the only <c>UserDepartments</c> route is a GET, no client code
    /// calls even that, and the sole delete path is a side effect of deleting the
    /// user outright.
    ///
    /// <c>DbInitializer</c> stopped writing these rows and now clears them at
    /// startup, but that is not enough for a deployment: <c>Seed:Enabled</c> is
    /// false in <c>appsettings.Production.json</c>, so the seeder never runs on the
    /// IIS host — <c>MigrateAsync</c> does. This is the half that actually reaches
    /// the deployed database, where the seeder had given
    /// <c>admin@annualleave.com</c> Engineering and left it permanently undeletable.
    ///
    /// Deleting rather than ignoring is the point: the foreign key is Restrict, so a
    /// row the pre-check chose to skip would still refuse the delete on SaveChanges,
    /// trading the explained 409 for the catch-all one.
    /// </summary>
    public partial class RemoveNonManagerUserDepartments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NormalizedName rather than Name: Identity writes the upper-cased form
            // itself, so it cannot have been edited to a different casing.
            migrationBuilder.Sql("""
                DELETE FROM [UserDepartments]
                WHERE [UserId] NOT IN (
                    SELECT [ur].[UserId]
                    FROM [AspNetUserRoles] AS [ur]
                    INNER JOIN [AspNetRoles] AS [r] ON [r].[Id] = [ur].[RoleId]
                    WHERE [r].[NormalizedName] = N'MANAGER'
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to restore. The deleted rows granted no access and no endpoint
            // could recreate them, so there is no state a rollback would want back.
        }
    }
}
