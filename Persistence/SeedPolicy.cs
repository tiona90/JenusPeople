namespace Persistence;

/// <summary>
/// What <see cref="DbInitializer"/> is permitted to do on this host.
///
/// The seeder plants a known constant password (<c>Pa$$w0rd</c>) into every
/// account it owns, and re-asserts it on every startup so that seeded logins stay
/// predictable in development. On a production database that is a standing
/// credential hole rather than a convenience: each restart hands the admin account
/// back to anyone who has read this repository, silently undoing a password the
/// operator has changed.
///
/// So the rule lives here, next to the seeder rather than at a call site that
/// could forget it: in the Production environment the seeder creates no accounts
/// and touches no passwords, and demo accounts are not seeded, unless
/// <c>Seed:AllowInProduction</c> is explicitly set — which is the deliberate
/// "yes, bootstrap this production database" switch.
/// </summary>
public sealed class SeedPolicy
{
    public const string ProductionEnvironmentName = "Production";

    private SeedPolicy(bool seedDemoData, bool manageSeedPasswords, bool restrictedForProduction)
    {
        SeedDemoData = seedDemoData;
        ManageSeedPasswords = manageSeedPasswords;
        RestrictedForProduction = restrictedForProduction;
    }

    /// <summary>
    /// Whether the demo manager/employee accounts and their business data are
    /// seeded. When false the seeder also deletes any that a previous run created,
    /// which is a cleanup worth doing in every environment.
    /// </summary>
    public bool SeedDemoData { get; }

    /// <summary>
    /// Whether the seeder may set a password: creating one of its accounts (which
    /// requires giving it the built-in default) or resetting an existing account's
    /// password back to that default. When false, existing accounts still get
    /// their display name, confirmed-email flag and role reconciled — none of
    /// which is a credential.
    /// </summary>
    public bool ManageSeedPasswords { get; }

    /// <summary>
    /// True when this policy was cut back because the host is Production and
    /// <c>Seed:AllowInProduction</c> was not set. The caller should log it: seeding
    /// was asked for and is being partly declined, which is worth saying out loud.
    /// </summary>
    public bool RestrictedForProduction { get; }

    /// <summary>
    /// The policy for a host, from its environment name and the <c>Seed:*</c>
    /// configuration.
    /// </summary>
    /// <param name="environmentName">
    /// <c>IHostEnvironment.EnvironmentName</c>. Compared case-insensitively, and an
    /// unrecognised or missing name is treated as non-production — the environment
    /// name is not a security boundary, <c>Seed:Enabled</c> is.
    /// </param>
    /// <param name="demoData">The <c>Seed:DemoData</c> setting.</param>
    /// <param name="allowInProduction">The <c>Seed:AllowInProduction</c> setting.</param>
    public static SeedPolicy For(string? environmentName, bool demoData, bool allowInProduction)
    {
        var isProduction = string.Equals(
            environmentName,
            ProductionEnvironmentName,
            StringComparison.OrdinalIgnoreCase);

        var restricted = isProduction && !allowInProduction;

        return new SeedPolicy(
            seedDemoData: demoData && !restricted,
            manageSeedPasswords: !restricted,
            restrictedForProduction: restricted);
    }

    /// <summary>
    /// The unrestricted policy, for development and tests. Named rather than
    /// implied by a default parameter so that no caller reaches it by accident.
    /// </summary>
    public static SeedPolicy Unrestricted(bool demoData = false) =>
        new(seedDemoData: demoData, manageSeedPasswords: true, restrictedForProduction: false);
}
