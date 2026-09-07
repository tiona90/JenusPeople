using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <summary>
    /// Makes <c>EmployeeProfiles.DepartmentId</c> nullable, and clears it for every
    /// user in the Admin role.
    ///
    /// An Admin sees every department, so belonging to one grants them nothing — and
    /// the admin panel has always agreed, hiding the whole Profile section for them.
    /// But the column was a required foreign key, so both write paths invented a
    /// value: the seeder gave <c>admin@annualleave.com</c> Engineering
    /// unconditionally, and <c>CreateUserDialog</c> substituted "the first active
    /// department" for a field it never showed.
    ///
    /// The invented row was not inert. It put the admin in that department's
    /// headcount and team strip on the Departments panel, in its "employees not
    /// checked in" warning and its leave-used figures, and in
    /// <c>DeleteDepartment</c>'s "employee" blocker count — so a department the
    /// admin had never worked in refused to be deleted, and nothing an admin could
    /// do would move them out, the field being hidden.
    ///
    /// <c>DbInitializer</c> now writes null and clears these at startup, but that
    /// does not reach a deployment: <c>Seed:Enabled</c> is false in
    /// <c>appsettings.Production.json</c>, so the seeder never runs on the IIS host
    /// — <c>MigrateAsync</c> does. This is the half that repairs the deployed
    /// database.
    ///
    /// <c>Timesheets.DepartmentId</c> becomes nullable in the same pass, because an
    /// Admin can file a timesheet and a required foreign key cannot take a
    /// department-less author. Existing rows keep the department they were filed
    /// under; that is history, and it is why the column is still indexed and still
    /// Restrict. <c>AnnualLeaves.DepartmentId</c> was already nullable.
    /// </summary>
    public partial class ClearAdminProfileDepartments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeProfiles_Departments_DepartmentId",
                table: "EmployeeProfiles");

            migrationBuilder.AlterColumn<int>(
                name: "DepartmentId",
                table: "Timesheets",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "DepartmentId",
                table: "EmployeeProfiles",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeProfiles_Departments_DepartmentId",
                table: "EmployeeProfiles",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // The data half. NormalizedName rather than Name: Identity writes the
            // upper-cased form itself, so it cannot have been edited to a different
            // casing. Timesheets are deliberately left alone — a filed sheet keeps
            // the department it was filed under.
            migrationBuilder.Sql("""
                UPDATE [EmployeeProfiles]
                SET [DepartmentId] = NULL
                WHERE [DepartmentId] IS NOT NULL
                  AND [UserId] IN (
                    SELECT [ur].[UserId]
                    FROM [AspNetUserRoles] AS [ur]
                    INNER JOIN [AspNetRoles] AS [r] ON [r].[Id] = [ur].[RoleId]
                    WHERE [r].[NormalizedName] = N'ADMIN'
                );
                """);
        }

        /// <summary>
        /// Restoring NOT NULL needs a department for every row that now has none.
        /// The <c>AlterColumn</c> calls below would supply <c>0</c>, which is not a
        /// department id, so re-adding the foreign key would then fail and leave the
        /// rollback half-applied. Filling in the lowest real department first is
        /// lossy — it reinstates exactly the invented assignment this migration
        /// removed — but that is what the old shape required, and a rollback that
        /// completes beats one that aborts partway.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeProfiles_Departments_DepartmentId",
                table: "EmployeeProfiles");

            migrationBuilder.Sql("""
                DECLARE @fallbackDepartmentId int = (
                    SELECT MIN([Id]) FROM [Departments]
                );

                UPDATE [EmployeeProfiles]
                SET [DepartmentId] = @fallbackDepartmentId
                WHERE [DepartmentId] IS NULL;

                UPDATE [Timesheets]
                SET [DepartmentId] = @fallbackDepartmentId
                WHERE [DepartmentId] IS NULL;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "DepartmentId",
                table: "Timesheets",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "DepartmentId",
                table: "EmployeeProfiles",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeProfiles_Departments_DepartmentId",
                table: "EmployeeProfiles",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
