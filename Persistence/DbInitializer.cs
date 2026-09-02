using Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Persistence;

public class DbInitializer
{
    private const string DefaultSeedPassword = "Pa$$w0rd";
    private static readonly Dictionary<string, string> LegacyRoleMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Author"] = AppRoles.Manager,
        ["Viewer"] = AppRoles.Employee
    };

    // Accounts from earlier versions — always removed on startup.
    private static readonly string[] DeprecatedSeedEmails =
    {
        "manager@annualleave.com",
        "employee@annualleave.com",
        "author@annualleave.com",
        "viewer@annualleave.com"
    };

    // Demo manager/employee accounts. Seeded only when demo data is enabled
    // (development); on a real deployment they're removed so only Admin remains.
    private static readonly string[] DemoSeedEmails =
    {
        "manager1@annualleave.com",
        "manager2@annualleave.com",
        "employee1a@annualleave.com",
        "employee1b@annualleave.com",
        "employee1c@annualleave.com",
        "employee1d@annualleave.com",
        "employee2a@annualleave.com",
        "employee2b@annualleave.com",
        "employee2c@annualleave.com",
        "employee2d@annualleave.com"
    };

    private record SeedUser(string DisplayName, string Email, string Role);

    /// <param name="policy">
    /// What this host allows the seeder to do. See <see cref="SeedPolicy"/> — on
    /// Production it withholds account creation and password resets, so pass
    /// <see cref="SeedPolicy.For"/>'s result rather than an unrestricted policy
    /// unless the caller is a test or a development tool.
    /// </param>
    public static async Task SeedData(AppDbContext context, UserManager<User> userManager,
        RoleManager<Role> roleManager, SeedPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        // ── Reference, structural and maintenance data ──────────────────────────
        //
        // Everything a real tenant needs regardless of whether it wants worked
        // examples: the role and type catalogues the app cannot function without,
        // the single app-settings row, the department list that every employee
        // profile hangs off, the admin's own assignment and profile, and the
        // backfills that repair rows predating a migration. This runs in every
        // environment.
        //
        // Order matters: each seeder bails out early when its dependencies are
        // missing, so the catalogues must land before the rows that reference them
        // — otherwise a fresh database needs several restarts to fill in.
        await SeedRoles(roleManager, userManager);
        await SeedUsers(context, userManager, policy);
        await SeedLeaveTypes(context);
        await BackfillLeaveTypeDesignFields(context);
        await SeedProjectActivityTypes(context);
        await SeedProjectComponents(context);
        await SeedDepartments(context);
        await SeedUserDepartments(context);
        await SeedEmployeeProfiles(context);
        // A no-op until projects exist, which is why it belongs here rather than
        // inside SeedProjects: the rows it repairs are real ones, and they need
        // repairing whether or not this host wants demo data.
        await BackfillProjectMetadata(context);
        await FixZeroEntitlementProfiles(context);
        await SeedAppSettings(context);

        // SeedUserDepartments and SeedEmployeeProfiles above each add demo rows too
        // — but only for demo users, and SeedUsers has already deleted those by this
        // point when the policy withholds them. So they self-limit to the admin's
        // own assignment and profile without needing the policy passed in.

        if (!policy.SeedDemoData)
        {
            return;
        }

        // ── Illustrative content ────────────────────────────────────────────────
        //
        // Three sample projects, plus a leave request, a timesheet and its entries
        // filed against them. No tenant asked for any of this. On a demo or UAT
        // site it is the point; on a live database it is indistinguishable from a
        // member of staff having booked leave and logged a week of hours they never
        // logged — so it is gated rather than left to degrade on its own. Only
        // SeedAnnualLeaves and SeedTimesheets would have degraded anyway, and only
        // by dropping their demo-user half; both still filed rows against the real
        // admin account, and SeedProjects seeded all three projects regardless of
        // which users existed.
        await SeedProjects(context);
        await SeedAnnualLeaves(context);
        await SeedTimesheets(context);
        await SeedTimesheetEntries(context);
    }

    private static async Task SeedTimesheets(AppDbContext context)
    {
        if (context.Timesheets.Any()) return;

        // Get admin user and profile
        var adminUser = context.Users.FirstOrDefault(u => u.Email == "admin@annualleave.com");
        if (adminUser is null) return;

        var adminProfile = context.EmployeeProfiles.FirstOrDefault(ep => ep.UserId == adminUser.Id);
        var engineering = context.Departments.FirstOrDefault(d => d.Code == "ENG");
        if (adminProfile is null || engineering is null) return;

        var timesheet = new Timesheet
        {
            EmployeeProfileId = adminProfile.Id,
            DepartmentId = engineering.Id,
            PeriodStart = DateTime.UtcNow.Date.AddDays(-7),
            PeriodEnd = DateTime.UtcNow.Date,
            TotalHours = 40,
            Status = TimesheetStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };

        await context.Timesheets.AddAsync(timesheet);
        await context.SaveChangesAsync();
    }

    private static async Task SeedTimesheetEntries(AppDbContext context)
    {
        if (context.TimesheetEntries.Any()) return;

        var timesheet = context.Timesheets.FirstOrDefault();
        var project = context.Projects.FirstOrDefault();
        if (timesheet is null || project is null) return;


        var entries = new List<TimesheetEntry>
            {
                new TimesheetEntry
                {
                    TimesheetId = timesheet.Id,
                    ProjectId = project.Id,
                    Date = DateTime.UtcNow.Date.AddDays(-2),
                    HoursWorked = 8,
                    Notes = "Worked on feature X."
                },
                new TimesheetEntry
                {
                    TimesheetId = timesheet.Id,
                    ProjectId = project.Id,
                    Date = DateTime.UtcNow.Date.AddDays(-1),
                    HoursWorked = 7.5m,
                    Notes = "Bug fixes and code review."
                }
            };

        await context.TimesheetEntries.AddRangeAsync(entries);
        await context.SaveChangesAsync();
    }
    // Demo only. BackfillProjectMetadata used to hang off the early return here;
    // SeedData calls it directly now so that hosts which skip the demo projects
    // still get their real project rows repaired.
    private static async Task SeedProjects(AppDbContext context)
    {
        if (context.Projects.Any()) return;

        var engineering = context.Departments.FirstOrDefault(d => d.Code == "ENG");
        var hr = context.Departments.FirstOrDefault(d => d.Code == "HR");
        var finance = context.Departments.FirstOrDefault(d => d.Code == "FIN");
        if (engineering is null || hr is null || finance is null) return;

        var admin = context.Users.FirstOrDefault(u => u.Email == "admin@annualleave.com");
        var manager1 = context.Users.FirstOrDefault(u => u.Email == "manager1@annualleave.com");
        var manager2 = context.Users.FirstOrDefault(u => u.Email == "manager2@annualleave.com");

        var projects = new List<Project>
        {
            new Project
            {
                Name = "Intranet Redesign", Code = "INTRA-001",
                Description = "Modernise the corporate intranet experience.",
                DepartmentAssignments = { new ProjectDepartment { DepartmentId = engineering.Id } },
                OwnerId = manager1?.Id ?? admin?.Id,
                Status = ProjectStatus.Active, IsActive = true,
                ColorKey = "p1", TargetWeeklyHours = 120, TargetMonthlyHours = 480
            },
            new Project
            {
                Name = "Payroll Automation", Code = "PAY-002",
                Description = "Automate payroll generation and approval flow.",
                DepartmentAssignments = { new ProjectDepartment { DepartmentId = finance.Id } },
                OwnerId = manager2?.Id ?? admin?.Id,
                Status = ProjectStatus.Active, IsActive = true,
                ColorKey = "p2", TargetWeeklyHours = 100, TargetMonthlyHours = 400
            },
            new Project
            {
                Name = "Recruitment Portal", Code = "REC-003",
                Description = "Candidate-facing portal for job applications.",
                DepartmentAssignments = { new ProjectDepartment { DepartmentId = hr.Id } },
                OwnerId = admin?.Id,
                Status = ProjectStatus.OnHold, IsActive = true,
                ColorKey = "p3", TargetWeeklyHours = 60, TargetMonthlyHours = 240
            }
        };

        await context.Projects.AddRangeAsync(projects);
        await context.SaveChangesAsync();
    }

    private static async Task BackfillProjectMetadata(AppDbContext context)
    {
        // Enrich pre-existing project rows that pre-date the metadata migration.
        var rows = await context.Projects.ToListAsync();
        var colors = new[] { "p1", "p2", "p3", "p4", "p5" };
        var admin = await context.Users.FirstOrDefaultAsync(u => u.Email == "admin@annualleave.com");
        var changed = false;
        var idx = 0;

        foreach (var p in rows)
        {
            var needsColor = string.IsNullOrEmpty(p.ColorKey) || p.ColorKey == "p1";
            var needsTargets = p.TargetWeeklyHours == 0 && p.TargetMonthlyHours == 0;
            var needsOwner = string.IsNullOrEmpty(p.OwnerId) && admin is not null;
            var needsStatus = p.Status == ProjectStatus.Active && !p.IsActive; // mismatch fix

            if (!needsColor && !needsTargets && !needsOwner && !needsStatus)
            {
                idx++;
                continue;
            }

            if (needsColor) p.ColorKey = colors[idx % colors.Length];
            if (needsTargets) { p.TargetWeeklyHours = 80; p.TargetMonthlyHours = 320; }
            if (needsOwner) p.OwnerId = admin!.Id;
            if (!p.IsActive) p.Status = ProjectStatus.Inactive;

            changed = true;
            idx++;
        }

        if (changed) await context.SaveChangesAsync();
    }

    private static async Task SeedRoles(RoleManager<Role> roleManager, UserManager<User> userManager)
    {
        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await EnsureIdentitySucceeded(
                    () => $"Failed to create role '{role}'.",
                    await roleManager.CreateAsync(new Role { Name = role }));
            }
        }

        foreach (var (legacyRole, replacementRole) in LegacyRoleMappings)
        {
            if (!await roleManager.RoleExistsAsync(legacyRole))
            {
                continue;
            }

            var usersInLegacyRole = await userManager.GetUsersInRoleAsync(legacyRole);
            foreach (var user in usersInLegacyRole)
            {
                if (!await userManager.IsInRoleAsync(user, replacementRole))
                {
                    await EnsureIdentitySucceeded(
                        () => $"Failed to add '{user.Email}' to role '{replacementRole}'.",
                        await userManager.AddToRoleAsync(user, replacementRole));
                }

                await EnsureIdentitySucceeded(
                    () => $"Failed to remove '{user.Email}' from role '{legacyRole}'.",
                    await userManager.RemoveFromRoleAsync(user, legacyRole));
            }

            var role = await roleManager.FindByNameAsync(legacyRole);
            if (role is not null)
            {
                await EnsureIdentitySucceeded(
                    () => $"Failed to delete legacy role '{legacyRole}'.",
                    await roleManager.DeleteAsync(role));
            }
        }
    }

    private static async Task SeedUsers(AppDbContext context, UserManager<User> userManager, SeedPolicy policy)
    {
        // Legacy accounts from older versions — always removed.
        await RemoveSeedUsersAsync(context, userManager, DeprecatedSeedEmails);

        // On a real deployment, strip the demo manager/employee accounts (in case
        // a previous deploy created them) so only the Admin account remains.
        if (!policy.SeedDemoData)
        {
            await RemoveSeedUsersAsync(context, userManager, DemoSeedEmails);
        }

        // Admin is always seeded; the demo managers/employees only in demo mode.
        var users = new List<SeedUser>
        {
            new("Admin User", "admin@annualleave.com", AppRoles.Admin),
        };

        if (policy.SeedDemoData)
        {
            users.AddRange(new[]
            {
                new SeedUser("Manager One", "manager1@annualleave.com", AppRoles.Manager),
                new SeedUser("Manager Two", "manager2@annualleave.com", AppRoles.Manager),
                new SeedUser("Employee 1A", "employee1a@annualleave.com", AppRoles.Employee),
                new SeedUser("Employee 1B", "employee1b@annualleave.com", AppRoles.Employee),
                new SeedUser("Employee 1C", "employee1c@annualleave.com", AppRoles.Employee),
                new SeedUser("Employee 1D", "employee1d@annualleave.com", AppRoles.Employee),
                new SeedUser("Employee 2A", "employee2a@annualleave.com", AppRoles.Employee),
                new SeedUser("Employee 2B", "employee2b@annualleave.com", AppRoles.Employee),
                new SeedUser("Employee 2C", "employee2c@annualleave.com", AppRoles.Employee),
                new SeedUser("Employee 2D", "employee2d@annualleave.com", AppRoles.Employee),
            });
        }

        foreach (var u in users)
        {
            var existingUser = await userManager.FindByEmailAsync(u.Email);

            if (existingUser is not null)
            {
                var shouldUpdateUser = false;

                if (!string.Equals(existingUser.DisplayName, u.DisplayName, StringComparison.Ordinal))
                {
                    existingUser.DisplayName = u.DisplayName;
                    shouldUpdateUser = true;
                }

                if (!existingUser.EmailConfirmed)
                {
                    existingUser.EmailConfirmed = true;
                    shouldUpdateUser = true;
                }

                if (!string.Equals(existingUser.Email, u.Email, StringComparison.OrdinalIgnoreCase))
                {
                    existingUser.Email = u.Email;
                    shouldUpdateUser = true;
                }

                if (!string.Equals(existingUser.UserName, u.Email, StringComparison.OrdinalIgnoreCase))
                {
                    existingUser.UserName = u.Email;
                    shouldUpdateUser = true;
                }

                if (shouldUpdateUser)
                {
                    await EnsureIdentitySucceeded(
                        () => $"Failed to update seed user '{u.Email}'.",
                        await userManager.UpdateAsync(existingUser));
                }

                if (!await userManager.IsInRoleAsync(existingUser, u.Role))
                {
                    await EnsureIdentitySucceeded(
                        () => $"Failed to add '{u.Email}' to role '{u.Role}'.",
                        await userManager.AddToRoleAsync(existingUser, u.Role));
                }

                // Keep seeded users deterministic across environments — except
                // where "deterministic" means "resets a live admin account to a
                // password published in this repository on every restart". The
                // reconciliation above is safe to repeat; this is not.
                if (policy.ManageSeedPasswords)
                {
                    await EnsurePassword(userManager, existingUser);
                }
            }
            else if (!policy.ManageSeedPasswords)
            {
                // Creating the account means giving it DefaultSeedPassword, which
                // this policy forbids. Leave it absent rather than plant a known
                // credential; an operator who wants the account bootstrapped sets
                // Seed:AllowInProduction. Every downstream seeder that references a
                // seed account already tolerates its absence.
                continue;
            }
            else
            {
                var user = new User
                {
                    DisplayName = u.DisplayName,
                    UserName = u.Email,
                    Email = u.Email,
                    EmailConfirmed = true
                };

                var createResult = await userManager.CreateAsync(user, DefaultSeedPassword);
                if (!createResult.Succeeded)
                {
                    throw new InvalidOperationException($"Failed to create seed user '{u.Email}': {string.Join(", ", createResult.Errors.Select(e => e.Description))}");
                }

                await EnsureIdentitySucceeded(
                    () => $"Failed to add '{u.Email}' to role '{u.Role}'.",
                    await userManager.AddToRoleAsync(user, u.Role));
            }
        }
    }

    // Deletes the given seed accounts (and their dependent rows) if they exist.
    private static async Task RemoveSeedUsersAsync(AppDbContext context, UserManager<User> userManager, IEnumerable<string> emails)
    {
        foreach (var email in emails)
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user is null) continue;

            await CleanupUserDependencies(context, user.Id, CancellationToken.None);

            var currentRoles = await userManager.GetRolesAsync(user);
            if (currentRoles.Count > 0)
            {
                await EnsureIdentitySucceeded(
                    () => $"Failed to remove roles for seed user '{email}'.",
                    await userManager.RemoveFromRolesAsync(user, currentRoles));
            }

            await EnsureIdentitySucceeded(
                () => $"Failed to delete seed user '{email}'.",
                await userManager.DeleteAsync(user));
        }
    }

    private static Task EnsureIdentitySucceeded(Func<string> errorMessage, IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"{errorMessage()} {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        return Task.CompletedTask;
    }

    private static async Task EnsurePassword(UserManager<User> userManager, User user)
    {
        if (await userManager.CheckPasswordAsync(user, DefaultSeedPassword))
            return;

        if (await userManager.HasPasswordAsync(user))
        {
            var removeResult = await userManager.RemovePasswordAsync(user);
            if (!removeResult.Succeeded)
            {
                throw new InvalidOperationException($"Failed to remove password for seed user '{user.Email}': {string.Join(", ", removeResult.Errors.Select(e => e.Description))}");
            }
        }

        var addResult = await userManager.AddPasswordAsync(user, DefaultSeedPassword);
        if (!addResult.Succeeded)
        {
            throw new InvalidOperationException($"Failed to set password for seed user '{user.Email}': {string.Join(", ", addResult.Errors.Select(e => e.Description))}");
        }
    }

    // Detaches everything pointing at a seed account so it can be deleted.
    //
    // Every FK handled here is DeleteBehavior.Restrict, which means the database
    // refuses the DELETE rather than tidying up after it: miss one and
    // RemoveSeedUsersAsync throws DbUpdateException("FOREIGN KEY constraint
    // failed"), Program.cs logs it, and the demo accounts survive with the
    // published password still on them — the exact opposite of what turning
    // Seed:DemoData off is supposed to achieve.
    //
    // This duplicates Application.AdminUsers.Commands.DeleteAdminUser, which is the
    // canonical version. The copies had drifted: this one was missing five of the
    // cases that one handles, so the DemoData true→false transition failed on the
    // demo projects owned by manager1/manager2. Keep them in step, and prefer
    // fixing DeleteAdminUser first — a case missing there is a user-facing 500.
    private static async Task CleanupUserDependencies(AppDbContext context, string userId, CancellationToken cancellationToken)
    {
        var userProfileId = await context.EmployeeProfiles
            .Where(ep => ep.UserId == userId)
            .Select(ep => ep.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(userProfileId))
        {
            var directReports = await context.EmployeeProfiles
                .Where(ep => ep.ManagerId == userProfileId)
                .ToListAsync(cancellationToken);

            foreach (var report in directReports)
            {
                report.ManagerId = null;
            }
        }

        var approvedLeaves = await context.AnnualLeaves
            .Where(al => al.ApprovedById == userId)
            .ToListAsync(cancellationToken);
        foreach (var leave in approvedLeaves)
        {
            leave.ApprovedById = null;
            leave.ApprovedAt = null;
        }

        // Leave this user was covering for someone else.
        var delegatedLeaves = await context.AnnualLeaves
            .Where(al => al.DelegateId == userId)
            .ToListAsync(cancellationToken);
        foreach (var leave in delegatedLeaves)
        {
            leave.DelegateId = null;
        }

        var assignedByRows = await context.UserDepartments
            .Where(ud => ud.AssignedByUserId == userId)
            .ToListAsync(cancellationToken);
        foreach (var row in assignedByRows)
        {
            row.AssignedByUserId = null;
        }

        var approvedTimesheets = await context.Timesheets
            .Where(t => t.ApproverId == userId)
            .ToListAsync(cancellationToken);
        foreach (var timesheet in approvedTimesheets)
        {
            timesheet.ApproverId = null;
            timesheet.ApprovedAt = null;
        }

        // The seeded demo projects are owned by manager1/manager2, so this is the
        // case that blocked turning demo data off on an already-seeded database.
        var ownedProjects = await context.Projects
            .Where(p => p.OwnerId == userId)
            .ToListAsync(cancellationToken);
        foreach (var project in ownedProjects)
        {
            project.OwnerId = null;
        }

        var timesheetStatusChangesByUser = await context.TimesheetStatusHistories
            .Where(h => h.ChangedByUserId == userId)
            .ToListAsync(cancellationToken);
        if (timesheetStatusChangesByUser.Count > 0)
        {
            context.TimesheetStatusHistories.RemoveRange(timesheetStatusChangesByUser);
        }

        // Timesheet.EmployeeProfileId points at the profile deleted below, and that FK is
        // Restrict as well, so the timesheets have to go first.
        if (!string.IsNullOrWhiteSpace(userProfileId))
        {
            var userTimesheets = await context.Timesheets
                .Where(t => t.EmployeeProfileId == userProfileId)
                .ToListAsync(cancellationToken);
            if (userTimesheets.Count > 0)
            {
                context.Timesheets.RemoveRange(userTimesheets);
            }
        }

        var statusChangesByUser = await context.LeaveStatusHistories
            .Where(h => h.ChangedByUserId == userId)
            .ToListAsync(cancellationToken);
        if (statusChangesByUser.Count > 0)
        {
            context.LeaveStatusHistories.RemoveRange(statusChangesByUser);
        }

        var ownedUserDepartments = await context.UserDepartments
            .Where(ud => ud.UserId == userId)
            .ToListAsync(cancellationToken);
        if (ownedUserDepartments.Count > 0)
        {
            context.UserDepartments.RemoveRange(ownedUserDepartments);
        }

        var employeeLeaves = await context.AnnualLeaves
            .Where(al => al.EmployeeId == userId)
            .ToListAsync(cancellationToken);
        if (employeeLeaves.Count > 0)
        {
            context.AnnualLeaves.RemoveRange(employeeLeaves);
        }

        var profile = await context.EmployeeProfiles
            .FirstOrDefaultAsync(ep => ep.UserId == userId, cancellationToken);
        if (profile is not null)
        {
            context.EmployeeProfiles.Remove(profile);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedAnnualLeaves(AppDbContext context)
    {
        if (context.AnnualLeaves.Any()) return;

        var adminUser = context.Users.FirstOrDefault(u => u.Email == "admin@annualleave.com");
        if (adminUser is null) return;

        // EmployeeProfileId has to be set alongside EmployeeId. CreateAnnualLeave
        // refuses to create a request without it, so every row the application
        // writes has one, and the balance calculators and the attendance boards
        // read that column rather than EmployeeId. A seeded row that left it null
        // would be a shape the rest of the code never has to handle.
        var adminProfile = context.EmployeeProfiles.FirstOrDefault(ep => ep.UserId == adminUser.Id);

        var annualLeaves = new List<AnnualLeave>
        {
            new AnnualLeave
            {
                Id = Guid.NewGuid().ToString(),
                EmployeeId = adminUser.Id,
                EmployeeProfileId = adminProfile?.Id,
                StartDate = DateTime.Now.AddMonths(1),
                EndDate = DateTime.Now.AddMonths(1).AddDays(5)
            }
        };

        // Second request belongs to a demo manager, so it only appears in demo mode.
        var managerUser = context.Users.FirstOrDefault(u => u.Email == "manager1@annualleave.com");
        if (managerUser is not null)
        {
            var managerProfile = context.EmployeeProfiles.FirstOrDefault(ep => ep.UserId == managerUser.Id);

            annualLeaves.Add(new AnnualLeave
            {
                Id = Guid.NewGuid().ToString(),
                EmployeeId = managerUser.Id,
                EmployeeProfileId = managerProfile?.Id,
                StartDate = DateTime.Now.AddMonths(2),
                EndDate = DateTime.Now.AddMonths(2).AddDays(10)
            });
        }

        await context.AnnualLeaves.AddRangeAsync(annualLeaves);
        await context.SaveChangesAsync();
    }

    private static async Task SeedProjectActivityTypes(AppDbContext context)
    {
        if (context.ProjectActivityTypes.Any()) return;

        var activityTypes = new List<ProjectActivityType>
        {
            new() { Name = "Development", Icon = "💻", ColorKey = "blue", Description = "Coding, implementation, and feature building.", IsActive = true },
            new() { Name = "Testing & QA", Icon = "🧪", ColorKey = "green", Description = "Quality assurance, test automation, and bug fixing.", IsActive = true },
            new() { Name = "Design", Icon = "🎨", ColorKey = "pink", Description = "UI/UX design, mockups, and design systems.", IsActive = true },
            new() { Name = "Documentation", Icon = "📝", ColorKey = "amber", Description = "Writing specs, guides, and technical documentation.", IsActive = true },
            new() { Name = "Code Review", Icon = "👀", ColorKey = "purple", Description = "Reviewing pull requests and peer code reviews.", IsActive = true },
            new() { Name = "Meetings & Sync", Icon = "👥", ColorKey = "red", Description = "Project meetings, standups, and collaboration.", IsActive = true },
            new() { Name = "Support & Fixes", Icon = "🆘", ColorKey = "orange", Description = "Bug fixes, hotfixes, and production support.", IsActive = false },
            new() { Name = "Research", Icon = "🔬", ColorKey = "cyan", Description = "Spike investigations, research, and exploration.", IsActive = true },
        };

        await context.ProjectActivityTypes.AddRangeAsync(activityTypes);
        await context.SaveChangesAsync();
    }

    private static async Task SeedProjectComponents(AppDbContext context)
    {
        if (context.ProjectComponents.Any()) return;

        var components = new List<ProjectComponent>
        {
            new() { Name = "DM", Icon = "🗄️", ColorKey = "blue", Description = "Data management — imports, exports, and data quality.", IsActive = true },
            new() { Name = "Lasernet", Icon = "🖨️", ColorKey = "green", Description = "Document output, forms, and distribution.", IsActive = true },
            new() { Name = "jDocs", Icon = "📄", ColorKey = "purple", Description = "Document generation and archiving.", IsActive = true },
        };

        await context.ProjectComponents.AddRangeAsync(components);
        await context.SaveChangesAsync();
    }

    private static async Task SeedLeaveTypes(AppDbContext context)
    {
        if (context.LeaveTypes.Any()) return;

        var leaveTypes = new List<LeaveType>
        {
            new LeaveType
            {
                Name = "Annual Leave", Icon = "🌴", ColorKey = "annual",
                Description = "Vacation days, holidays, and personal time off.",
                RequiresApproval = true, IsActive = true, AffectsBalance = true, Paid = true,
                AttachmentPolicy = AttachmentPolicy.None,
                DefaultAllowance = 25, AllowanceUnit = "days/year",
                AccrualNotes = "Resets 1 Jan · No carryover",
                MinNoticeDays = 7, MaxConsecutiveDays = 15, HalfDayAllowed = true,
                EligibilityNotes = "All employees", EligibilityScope = EligibilityScope.All
            },
            new LeaveType
            {
                Name = "Sick Leave", Icon = "🤒", ColorKey = "sick",
                Description = "Time off due to illness or medical appointments.",
                RequiresApproval = true, IsActive = true, AffectsBalance = false, Paid = true,
                AttachmentPolicy = AttachmentPolicy.Optional,
                DefaultAllowance = 10, AllowanceUnit = "days/year",
                AccrualNotes = "Resets 1 Jan · 5 days carryover allowed",
                MinNoticeDays = 0, MaxConsecutiveDays = 30, HalfDayAllowed = true,
                EligibilityNotes = "All employees", EligibilityScope = EligibilityScope.All
            },
            new LeaveType
            {
                Name = "Personal Days", Icon = "🏠", ColorKey = "personal",
                Description = "Family matters, errands, or personal appointments.",
                RequiresApproval = true, IsActive = true, AffectsBalance = false, Paid = true,
                AttachmentPolicy = AttachmentPolicy.None,
                DefaultAllowance = 3, AllowanceUnit = "days/year",
                AccrualNotes = "Resets 1 Jan · No carryover",
                MinNoticeDays = 1, MaxConsecutiveDays = 3, HalfDayAllowed = true,
                EligibilityNotes = "All employees", EligibilityScope = EligibilityScope.All
            },
            new LeaveType
            {
                Name = "Bereavement", Icon = "🕊️", ColorKey = "bereavement",
                Description = "Time off following the loss of a loved one.",
                RequiresApproval = true, IsActive = true, AffectsBalance = false, Paid = true,
                AttachmentPolicy = AttachmentPolicy.Optional,
                DefaultAllowance = 5, AllowanceUnit = "days/event",
                AccrualNotes = "Granted per event · No annual limit",
                MinNoticeDays = 0, MaxConsecutiveDays = 5, HalfDayAllowed = false,
                EligibilityNotes = "All employees", EligibilityScope = EligibilityScope.All
            },
            new LeaveType
            {
                Name = "Maternity Leave", Icon = "👶", ColorKey = "maternity",
                Description = "Time off for new mothers around the birth of a child.",
                RequiresApproval = true, IsActive = true, AffectsBalance = false, Paid = true,
                AttachmentPolicy = AttachmentPolicy.Required,
                DefaultAllowance = 90, AllowanceUnit = "days/event",
                AccrualNotes = "Granted per event · Once per pregnancy",
                MinNoticeDays = 30, MaxConsecutiveDays = 90, HalfDayAllowed = false,
                EligibilityNotes = "Female employees", EligibilityScope = EligibilityScope.Limited
            },
            new LeaveType
            {
                Name = "Paternity Leave", Icon = "👨‍👶", ColorKey = "paternity",
                Description = "Time off for new fathers around the birth of a child.",
                RequiresApproval = true, IsActive = true, AffectsBalance = false, Paid = true,
                AttachmentPolicy = AttachmentPolicy.Required,
                DefaultAllowance = 14, AllowanceUnit = "days/event",
                AccrualNotes = "Granted per event · Once per child",
                MinNoticeDays = 30, MaxConsecutiveDays = 14, HalfDayAllowed = false,
                EligibilityNotes = "Male employees", EligibilityScope = EligibilityScope.Limited
            },
            new LeaveType
            {
                Name = "Unpaid Leave", Icon = "💼", ColorKey = "unpaid",
                Description = "Extended time off without pay or balance deduction.",
                RequiresApproval = true, IsActive = true, AffectsBalance = false, Paid = false,
                AttachmentPolicy = AttachmentPolicy.None,
                DefaultAllowance = 30, AllowanceUnit = "days/year",
                AccrualNotes = "No annual limit · Manager + HR approval",
                MinNoticeDays = 14, MaxConsecutiveDays = 30, HalfDayAllowed = false,
                EligibilityNotes = "Employees after 1yr", EligibilityScope = EligibilityScope.Limited
            },
            new LeaveType
            {
                Name = "Sabbatical", Icon = "🎓", ColorKey = "default",
                Description = "Extended career break for study, travel, or research.",
                RequiresApproval = true, IsActive = false, AffectsBalance = false, Paid = false,
                AttachmentPolicy = AttachmentPolicy.None,
                DefaultAllowance = 90, AllowanceUnit = "days/5 years",
                AccrualNotes = "After 5 years of service · Once per period",
                MinNoticeDays = 60, MaxConsecutiveDays = 90, HalfDayAllowed = false,
                EligibilityNotes = "Tenured employees (5+ years)", EligibilityScope = EligibilityScope.Limited
            },
        };

        await context.LeaveTypes.AddRangeAsync(leaveTypes);
        await context.SaveChangesAsync();
    }

    private static async Task BackfillLeaveTypeDesignFields(AppDbContext context)
    {
        // Enrich existing rows that pre-date the design-fields migration with realistic defaults.
        var defaults = new Dictionary<string, LeaveType>(StringComparer.OrdinalIgnoreCase)
        {
            ["Annual Leave"] = new() { Icon = "🌴", ColorKey = "annual", Description = "Vacation days, holidays, and personal time off.", Paid = true, AttachmentPolicy = AttachmentPolicy.None, DefaultAllowance = 25, AllowanceUnit = "days/year", AccrualNotes = "Resets 1 Jan · No carryover", MinNoticeDays = 7, MaxConsecutiveDays = 15, HalfDayAllowed = true, EligibilityNotes = "All employees", EligibilityScope = EligibilityScope.All },
            ["Sick Leave"] = new() { Icon = "🤒", ColorKey = "sick", Description = "Time off due to illness or medical appointments.", Paid = true, AttachmentPolicy = AttachmentPolicy.Optional, DefaultAllowance = 10, AllowanceUnit = "days/year", AccrualNotes = "Resets 1 Jan · 5 days carryover allowed", MinNoticeDays = 0, MaxConsecutiveDays = 30, HalfDayAllowed = true, EligibilityNotes = "All employees", EligibilityScope = EligibilityScope.All },
            ["Personal Days"] = new() { Icon = "🏠", ColorKey = "personal", Description = "Family matters, errands, or personal appointments.", Paid = true, AttachmentPolicy = AttachmentPolicy.None, DefaultAllowance = 3, AllowanceUnit = "days/year", AccrualNotes = "Resets 1 Jan · No carryover", MinNoticeDays = 1, MaxConsecutiveDays = 3, HalfDayAllowed = true, EligibilityNotes = "All employees", EligibilityScope = EligibilityScope.All },
            ["Bereavement"] = new() { Icon = "🕊️", ColorKey = "bereavement", Description = "Time off following the loss of a loved one.", Paid = true, AttachmentPolicy = AttachmentPolicy.Optional, DefaultAllowance = 5, AllowanceUnit = "days/event", AccrualNotes = "Granted per event · No annual limit", MinNoticeDays = 0, MaxConsecutiveDays = 5, HalfDayAllowed = false, EligibilityNotes = "All employees", EligibilityScope = EligibilityScope.All },
            ["Compassionate Leave"] = new() { Icon = "🕊️", ColorKey = "bereavement", Description = "Time off following the loss of a loved one.", Paid = true, AttachmentPolicy = AttachmentPolicy.Optional, DefaultAllowance = 5, AllowanceUnit = "days/event", AccrualNotes = "Granted per event · No annual limit", MinNoticeDays = 0, MaxConsecutiveDays = 5, HalfDayAllowed = false, EligibilityNotes = "All employees", EligibilityScope = EligibilityScope.All },
            ["Maternity Leave"] = new() { Icon = "👶", ColorKey = "maternity", Description = "Time off for new mothers around the birth of a child.", Paid = true, AttachmentPolicy = AttachmentPolicy.Required, DefaultAllowance = 90, AllowanceUnit = "days/event", AccrualNotes = "Granted per event · Once per pregnancy", MinNoticeDays = 30, MaxConsecutiveDays = 90, HalfDayAllowed = false, EligibilityNotes = "Female employees", EligibilityScope = EligibilityScope.Limited },
            ["Paternity Leave"] = new() { Icon = "👨‍👶", ColorKey = "paternity", Description = "Time off for new fathers around the birth of a child.", Paid = true, AttachmentPolicy = AttachmentPolicy.Required, DefaultAllowance = 14, AllowanceUnit = "days/event", AccrualNotes = "Granted per event · Once per child", MinNoticeDays = 30, MaxConsecutiveDays = 14, HalfDayAllowed = false, EligibilityNotes = "Male employees", EligibilityScope = EligibilityScope.Limited },
            ["Unpaid Leave"] = new() { Icon = "💼", ColorKey = "unpaid", Description = "Extended time off without pay or balance deduction.", Paid = false, AttachmentPolicy = AttachmentPolicy.None, DefaultAllowance = 30, AllowanceUnit = "days/year", AccrualNotes = "No annual limit · Manager + HR approval", MinNoticeDays = 14, MaxConsecutiveDays = 30, HalfDayAllowed = false, EligibilityNotes = "Employees after 1yr", EligibilityScope = EligibilityScope.Limited },
            ["Sabbatical"] = new() { Icon = "🎓", ColorKey = "default", Description = "Extended career break for study, travel, or research.", Paid = false, AttachmentPolicy = AttachmentPolicy.None, DefaultAllowance = 90, AllowanceUnit = "days/5 years", AccrualNotes = "After 5 years of service · Once per period", MinNoticeDays = 60, MaxConsecutiveDays = 90, HalfDayAllowed = false, EligibilityNotes = "Tenured employees (5+ years)", EligibilityScope = EligibilityScope.Limited },
        };

        var rows = await context.LeaveTypes.ToListAsync();
        var changed = false;

        foreach (var row in rows)
        {
            // Only fill rows that look uninitialised (still on schema defaults).
            var looksEmpty = string.IsNullOrEmpty(row.Description) && row.DefaultAllowance == 0;
            if (!looksEmpty) continue;

            if (!defaults.TryGetValue(row.Name, out var preset)) continue;

            row.Icon = preset.Icon;
            row.ColorKey = preset.ColorKey;
            row.Description = preset.Description;
            row.Paid = preset.Paid;
            row.AttachmentPolicy = preset.AttachmentPolicy;
            row.DefaultAllowance = preset.DefaultAllowance;
            row.AllowanceUnit = preset.AllowanceUnit;
            row.AccrualNotes = preset.AccrualNotes;
            row.MinNoticeDays = preset.MinNoticeDays;
            row.MaxConsecutiveDays = preset.MaxConsecutiveDays;
            row.HalfDayAllowed = preset.HalfDayAllowed;
            row.EligibilityNotes = preset.EligibilityNotes;
            row.EligibilityScope = preset.EligibilityScope;
            changed = true;
        }

        if (changed) await context.SaveChangesAsync();
    }

    private static async Task SeedDepartments(AppDbContext context)
    {
        if (context.Departments.Any()) return;

        var departments = new List<Department>
        {
            new Department { Name = "Engineering",       Code = "ENG",  IsActive = true },
            new Department { Name = "Human Resources",   Code = "HR",   IsActive = true },
            new Department { Name = "Finance",           Code = "FIN",  IsActive = true },
            new Department { Name = "Marketing",         Code = "MKT",  IsActive = true },
            new Department { Name = "Operations",        Code = "OPS",  IsActive = true },
        };

        await context.Departments.AddRangeAsync(departments);
        await context.SaveChangesAsync();
    }

    private static async Task SeedUserDepartments(AppDbContext context)
    {
        if (context.UserDepartments.Any()) return;

        var adminUser = context.Users.FirstOrDefault(u => u.Email == "admin@annualleave.com");
        if (adminUser is null) return;

        var engineering = context.Departments.FirstOrDefault(d => d.Code == "ENG");
        var hr = context.Departments.FirstOrDefault(d => d.Code == "HR");
        if (engineering is null || hr is null) return;

        var userDepartments = new List<UserDepartment>
        {
            new UserDepartment
            {
                UserId         = adminUser.Id,
                DepartmentId   = engineering.Id,
                AssignedByUserId = adminUser.Id,
                AssignedAt     = DateTime.UtcNow
            },
        };

        // Demo assignments — these users exist only when demo data is enabled.
        void Assign(string email, int departmentId)
        {
            var user = context.Users.FirstOrDefault(u => u.Email == email);
            if (user is null) return;

            userDepartments.Add(new UserDepartment
            {
                UserId         = user.Id,
                DepartmentId   = departmentId,
                AssignedByUserId = adminUser.Id,
                AssignedAt     = DateTime.UtcNow
            });
        }

        Assign("manager1@annualleave.com", engineering.Id);
        Assign("employee1a@annualleave.com", hr.Id);

        await context.UserDepartments.AddRangeAsync(userDepartments);
        await context.SaveChangesAsync();
    }

    private static async Task SeedEmployeeProfiles(AppDbContext context)
    {
        if (context.EmployeeProfiles.Any()) return;

        var adminUser = context.Users.FirstOrDefault(u => u.Email == "admin@annualleave.com");
        var engineering = context.Departments.FirstOrDefault(d => d.Code == "ENG");
        var finance = context.Departments.FirstOrDefault(d => d.Code == "FIN");
        if (adminUser is null || engineering is null || finance is null) return;

        // Admin profile — no manager (top of hierarchy). Always seeded.
        var adminProfile = new EmployeeProfile
        {
            Id = Guid.NewGuid().ToString(),
            UserId = adminUser.Id,
            DepartmentId = engineering.Id,
            ManagerId = null,
            JobTitle = "Engineering Manager",
            AnnualLeaveEntitlement = 20,
            CreatedAt = DateTime.UtcNow
        };

        var profiles = new List<EmployeeProfile> { adminProfile };

        // Demo profiles — only for demo users that were actually seeded.
        EmployeeProfile? AddManager(string email, int departmentId, string jobTitle)
        {
            var user = context.Users.FirstOrDefault(u => u.Email == email);
            if (user is null) return null;
            var profile = new EmployeeProfile
            {
                Id = Guid.NewGuid().ToString(),
                UserId = user.Id,
                DepartmentId = departmentId,
                ManagerId = adminProfile.Id,
                JobTitle = jobTitle,
                AnnualLeaveEntitlement = 20,
                CreatedAt = DateTime.UtcNow
            };
            profiles.Add(profile);
            return profile;
        }

        void AddEmployee(string email, int departmentId, string? managerProfileId, string jobTitle)
        {
            var user = context.Users.FirstOrDefault(u => u.Email == email);
            if (user is null) return;
            profiles.Add(new EmployeeProfile
            {
                Id = Guid.NewGuid().ToString(),
                UserId = user.Id,
                DepartmentId = departmentId,
                ManagerId = managerProfileId,
                JobTitle = jobTitle,
                AnnualLeaveEntitlement = 20,
                CreatedAt = DateTime.UtcNow
            });
        }

        var manager1Profile = AddManager("manager1@annualleave.com", engineering.Id, "Engineering Team Lead");
        var manager2Profile = AddManager("manager2@annualleave.com", finance.Id, "Finance Team Lead");

        AddEmployee("employee1a@annualleave.com", engineering.Id, manager1Profile?.Id, "Engineer");
        AddEmployee("employee1b@annualleave.com", engineering.Id, manager1Profile?.Id, "Engineer");
        AddEmployee("employee1c@annualleave.com", engineering.Id, manager1Profile?.Id, "Engineer");
        AddEmployee("employee1d@annualleave.com", engineering.Id, manager1Profile?.Id, "Engineer");
        AddEmployee("employee2a@annualleave.com", finance.Id, manager2Profile?.Id, "Accountant");
        AddEmployee("employee2b@annualleave.com", finance.Id, manager2Profile?.Id, "Accountant");
        AddEmployee("employee2c@annualleave.com", finance.Id, manager2Profile?.Id, "Accountant");
        AddEmployee("employee2d@annualleave.com", finance.Id, manager2Profile?.Id, "Accountant");

        await context.EmployeeProfiles.AddRangeAsync(profiles);
        await context.SaveChangesAsync();

        foreach (var profile in profiles)
        {
            await context.Entry(profile).ReloadAsync();
            profile.LeaveBalance = profile.AnnualLeaveEntitlement;
        }

        await context.SaveChangesAsync();
    }

    private static async Task SeedAppSettings(AppDbContext context)
    {
        if (await context.AppSettings.AnyAsync()) return;
        context.AppSettings.Add(new AppSettings
        {
            LeaveYearStartMonth = 1,
            HolidayCountryCode = "CY",
            HolidayCountryName = "Cyprus",
        });
        await context.SaveChangesAsync();
    }

    // Runs on every startup — brings any profile with entitlement=0 up to 20 days.
    private static async Task FixZeroEntitlementProfiles(AppDbContext context)
    {
        var profiles = await context.EmployeeProfiles
            .Where(ep => ep.AnnualLeaveEntitlement == 0)
            .ToListAsync();

        if (profiles.Count == 0) return;

        foreach (var profile in profiles)
        {
            profile.AnnualLeaveEntitlement = 20;
            profile.LeaveBalance = 20;
        }

        await context.SaveChangesAsync();
    }

}


