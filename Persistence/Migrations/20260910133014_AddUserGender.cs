using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <summary>
    /// Records a user's gender: a nullable int on AspNetUsers, null meaning "not
    /// specified". Nothing consults it — see the comment on <c>Domain.User.Gender</c>.
    ///
    /// The SlackEnabled drop below is not part of that feature. It is pre-existing
    /// model drift: the property was removed from the AppSettings entity in 930160f
    /// without a migration, so every migration scaffolded since has picked the drop
    /// up, and this is simply the first one on this branch. It is guarded rather
    /// than a plain DropColumn because another branch in flight
    /// (feature/per-child-paternity-leave) carries the same drop, and whichever ran
    /// second would fail on a column that was already gone.
    ///
    /// The default constraint has to go first, and by name. SlackEnabled is
    /// <c>bit NOT NULL DEFAULT 0</c>, so SQL Server has an auto-named
    /// <c>DF__AppSettings__…</c> constraint bound to it and refuses to drop the
    /// column while it exists ("one or more objects access this column", error
    /// 5074). EF's own DropColumn emits that DROP CONSTRAINT for you; hand-written
    /// SQL has to do it explicitly, and cannot hard-code the name because SQL
    /// Server generated it per database.
    /// </summary>
    public partial class AddUserGender : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF COL_LENGTH('AppSettings', 'SlackEnabled') IS NOT NULL
                BEGIN
                    DECLARE @constraint sysname;

                    SELECT @constraint = dc.name
                    FROM sys.default_constraints dc
                    INNER JOIN sys.columns c
                        ON c.object_id = dc.parent_object_id
                        AND c.column_id = dc.parent_column_id
                    WHERE dc.parent_object_id = OBJECT_ID(N'[AppSettings]')
                        AND c.name = N'SlackEnabled';

                    IF @constraint IS NOT NULL
                        EXEC(N'ALTER TABLE [AppSettings] DROP CONSTRAINT [' + @constraint + N']');

                    ALTER TABLE [AppSettings] DROP COLUMN [SlackEnabled];
                END
            ");

            migrationBuilder.AddColumn<int>(
                name: "Gender",
                table: "AspNetUsers",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Gender",
                table: "AspNetUsers");

            migrationBuilder.Sql(@"
                IF COL_LENGTH('AppSettings', 'SlackEnabled') IS NULL
                    ALTER TABLE [AppSettings] ADD [SlackEnabled] bit NOT NULL DEFAULT 0;
            ");
        }
    }
}
