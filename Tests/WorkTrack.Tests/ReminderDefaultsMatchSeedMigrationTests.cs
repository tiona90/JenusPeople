using Application.Settings.Support;
using Persistence.Migrations;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// <c>SeedReminderRunsOnUpgrade</c> stamps every reminder as handled on the day
/// it runs, from a list it hard-codes — a migration has to say what it did, not
/// read a catalogue that may change under it. This keeps that list and
/// <see cref="ReminderDefaults"/> the same, so a reminder added to the
/// catalogue after the migration is a deliberate omission, not a forgotten one:
/// a reminder missing from the seed goes out on the deploy day as a catch-up,
/// which is right for a brand-new reminder and wrong for an existing one.
/// </summary>
public class ReminderDefaultsMatchSeedMigrationTests
{
    [Fact]
    public void The_seed_migration_names_every_reminder_in_the_catalogue()
    {
        var catalogue = ReminderDefaults.Create().Select(r => r.Id).OrderBy(id => id).ToArray();
        var seeded = SeedReminderRunsOnUpgrade.ReminderIds.OrderBy(id => id).ToArray();

        Assert.Equal(catalogue, seeded);
    }

    [Fact]
    public void The_seed_outcome_fits_the_column()
    {
        Assert.True(SeedReminderRunsOnUpgrade.Outcome.Length <= Domain.ReminderRun.OutcomeMaxLength);
        Assert.DoesNotContain("'", SeedReminderRunsOnUpgrade.Outcome); // it is spliced into SQL
    }
}
