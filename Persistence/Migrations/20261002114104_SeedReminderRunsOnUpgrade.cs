using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <summary>
    /// Stamps every reminder as already handled on the day the upgrade runs.
    ///
    /// <c>AddReminderRuns</c> created the table that stops a reminder going out
    /// twice in a day, but it starts empty, and the previous version kept "already
    /// sent today" in memory — gone with the process. So the first start after the
    /// deploy saw nothing recorded, took every slot earlier that day as missed and
    /// caught them all up: the pending-approvals digest, the attendance report and
    /// nine check-in reminders went out a second time on the day it was first run.
    /// Recording the day as handled here means the first start sends nothing it
    /// should not; the next day runs normally.
    ///
    /// The date is the UTC date, not the org-local one — plain SQL has no access to
    /// the time-zone setting. Between 21:00 UTC and local midnight at UTC+3 that
    /// stamps yesterday, which merely leaves the old behaviour for that window; a
    /// deploy happens in working hours. Rows already present (an instance that
    /// ran the previous migration before this one existed) are left alone.
    /// </summary>
    public partial class SeedReminderRunsOnUpgrade : Migration
    {
        // Hard-coded on purpose: a migration must say what it did, not what the
        // catalogue says today. ReminderDefaultsMatchSeedMigrationTests pins the
        // two lists to each other.
        public static readonly string[] ReminderIds =
        {
            "pending-approvals",
            "late-submissions",
            "daily-attendance-report",
            "low-balance",
            "department-digest",
            "birthday-reminder",
            "check-in",
            "check-out",
        };

        public const string Outcome = "assumed sent today by the previous version (recorded on upgrade)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var values = string.Join(", ", ReminderIds.Select(id => $"('{id}')"));
            migrationBuilder.Sql($"""
                INSERT INTO [ReminderRuns] ([ReminderId], [RanOn], [RanAtUtc], [Outcome])
                SELECT v.[Id], CAST(GETUTCDATE() AS date), GETUTCDATE(), N'{Outcome}'
                FROM (VALUES {values}) AS v([Id])
                WHERE NOT EXISTS (SELECT 1 FROM [ReminderRuns] r WHERE r.[ReminderId] = v.[Id]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DELETE FROM [ReminderRuns] WHERE [Outcome] = N'{Outcome}';");
        }
    }
}
