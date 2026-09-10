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
    /// </summary>
    public partial class AddUserGender : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF COL_LENGTH('AppSettings', 'SlackEnabled') IS NOT NULL
                    ALTER TABLE [AppSettings] DROP COLUMN [SlackEnabled];
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
