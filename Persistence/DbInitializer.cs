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

    public static async Task SeedData(AppDbContext context, UserManager<User> userManager,
        RoleManager<Role> roleManager)
    {
        await SeedRoles(roleManager, userManager);
        await SeedUsers(context, userManager);
        await SeedAnnualLeaves(context);
        await SeedTimesheets(context);
        await SeedLeaveTypes(context);
        await BackfillLeaveTypeDesignFields(context);
        await SeedProjectActivityTypes(context);
        await SeedDepartments(context);
        await SeedProjects(context);
        await SeedUserDepartments(context);
        await SeedEmployeeProfiles(context);
        await SeedTimesheetEntries(context);
        await FixZeroEntitlementProfiles(context);
        await SeedAppSettings(context);
    }

    private static async Task SeedTimesheets(AppDbContext context)
    {
        if (context.Timesheets.Any()) return;

        // Get admin user and profile
        var adminUser = context.Users.FirstOrDefault(u => u.Email == "admin@annualleave.com");
        var adminProfile = context.EmployeeProfiles.FirstOrDefault(ep => ep.UserId == adminUser.Id);
        var engineering = context.Departments.FirstOrDefault(d => d.Code == "ENG");
        if (adminUser is null || adminProfile is null || engineering is null) return;

        var timesheet = new Timesheet
        {
            EmployeeId = adminProfile.Id,
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
    private static async Task SeedProjects(AppDbContext context)
    {
        if (context.Projects.Any())
        {
            await BackfillProjectMetadata(context);
            return;
        }

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
                DepartmentId = engineering.Id, OwnerId = manager1?.Id ?? admin?.Id,
                Status = ProjectStatus.Active, IsActive = true,
                ColorKey = "p1", TargetWeeklyHours = 120, TargetMonthlyHours = 480
            },
            new Project
            {
                Name = "Payroll Automation", Code = "PAY-002",
                Description = "Automate payroll generation and approval flow.",
                DepartmentId = finance.Id, OwnerId = manager2?.Id ?? admin?.Id,
                Status = ProjectStatus.Active, IsActive = true,
                ColorKey = "p2", TargetWeeklyHours = 100, TargetMonthlyHours = 400
            },
            new Project
            {
                Name = "Recruitment Portal", Code = "REC-003",
                Description = "Candidate-facing portal for job applications.",
                DepartmentId = hr.Id, OwnerId = admin?.Id,
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

    private static async Task SeedUsers(AppDbContext context, UserManager<User> userManager)
    {
        // Remove legacy seeded accounts so only admin stays as a default user.
        var deprecatedSeedEmails = new[]
        {
            "manager@annualleave.com",
            "employee@annualleave.com",
            "author@annualleave.com",
            "viewer@annualleave.com"
        };

        foreach (var deprecatedEmail in deprecatedSeedEmails)
        {
            var deprecatedUser = await userManager.FindByEmailAsync(deprecatedEmail);
            if (deprecatedUser is null) continue;

            await CleanupUserDependencies(context, deprecatedUser.Id, CancellationToken.None);

            var currentRoles = await userManager.GetRolesAsync(deprecatedUser);
            if (currentRoles.Count > 0)
            {
                await EnsureIdentitySucceeded(
                    () => $"Failed to remove roles for deprecated seed user '{deprecatedEmail}'.",
                    await userManager.RemoveFromRolesAsync(deprecatedUser, currentRoles));
            }

            await EnsureIdentitySucceeded(
                () => $"Failed to delete deprecated seed user '{deprecatedEmail}'.",
                await userManager.DeleteAsync(deprecatedUser));
        }


        // --- Custom test users: 2 managers, 4 employees each, all emails confirmed ---
        var users = new[]
        {
            new { DisplayName = "Admin User", Email = "admin@annualleave.com", LegacyEmail = (string?)null, Role = AppRoles.Admin },
            new { DisplayName = "Manager One", Email = "manager1@annualleave.com", LegacyEmail = (string?)null, Role = AppRoles.Manager },
            new { DisplayName = "Manager Two", Email = "manager2@annualleave.com", LegacyEmail = (string?)null, Role = AppRoles.Manager },
            new { DisplayName = "Employee 1A", Email = "employee1a@annualleave.com", LegacyEmail = (string?)null, Role = AppRoles.Employee },
            new { DisplayName = "Employee 1B", Email = "employee1b@annualleave.com", LegacyEmail = (string?)null, Role = AppRoles.Employee },
            new { DisplayName = "Employee 1C", Email = "employee1c@annualleave.com", LegacyEmail = (string?)null, Role = AppRoles.Employee },
            new { DisplayName = "Employee 1D", Email = "employee1d@annualleave.com", LegacyEmail = (string?)null, Role = AppRoles.Employee },
            new { DisplayName = "Employee 2A", Email = "employee2a@annualleave.com", LegacyEmail = (string?)null, Role = AppRoles.Employee },
            new { DisplayName = "Employee 2B", Email = "employee2b@annualleave.com", LegacyEmail = (string?)null, Role = AppRoles.Employee },
            new { DisplayName = "Employee 2C", Email = "employee2c@annualleave.com", LegacyEmail = (string?)null, Role = AppRoles.Employee },
            new { DisplayName = "Employee 2D", Email = "employee2d@annualleave.com", LegacyEmail = (string?)null, Role = AppRoles.Employee },
        };

        foreach (var u in users)
        {
            var existingUser = await userManager.FindByEmailAsync(u.Email);
            if (existingUser is null && !string.IsNullOrWhiteSpace(u.LegacyEmail))
            {
                existingUser = await userManager.FindByEmailAsync(u.LegacyEmail);
            }

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

                // Keep seeded users deterministic across environments.
                await EnsurePassword(userManager, existingUser);
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

        var assignedByRows = await context.UserDepartments
            .Where(ud => ud.AssignedByUserId == userId)
            .ToListAsync(cancellationToken);
        foreach (var row in assignedByRows)
        {
            row.AssignedByUserId = null;
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
        var managerUser = context.Users.FirstOrDefault(u => u.Email == "manager@annualleave.com");
        if (adminUser is null || managerUser is null) return;

        var annualLeaves = new List<AnnualLeave>
        {
            new AnnualLeave
            {
                Id = Guid.NewGuid().ToString(),
                EmployeeId = adminUser.Id,
                StartDate = DateTime.Now.AddMonths(1),
                EndDate = DateTime.Now.AddMonths(1).AddDays(5)
            },
            new AnnualLeave
            {
                Id = Guid.NewGuid().ToString(),
                EmployeeId = managerUser.Id,
                StartDate = DateTime.Now.AddMonths(2),
                EndDate = DateTime.Now.AddMonths(2).AddDays(10)
            }
        };

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
        var managerUser = context.Users.FirstOrDefault(u => u.Email == "manager@annualleave.com");
        var employeeUser = context.Users.FirstOrDefault(u => u.Email == "employee@annualleave.com");
        if (adminUser is null || managerUser is null || employeeUser is null) return;

        var engineering = context.Departments.FirstOrDefault(d => d.Code == "ENG");
        var hr = context.Departments.FirstOrDefault(d => d.Code == "HR");
        var finance = context.Departments.FirstOrDefault(d => d.Code == "FIN");
        if (engineering is null || hr is null || finance is null) return;

        var userDepartments = new List<UserDepartment>
        {
            new UserDepartment
            {
                UserId         = adminUser.Id,
                DepartmentId   = engineering.Id,
                AssignedByUserId = adminUser.Id,
                AssignedAt     = DateTime.UtcNow
            },
            new UserDepartment
            {
                UserId         = managerUser.Id,
                DepartmentId   = engineering.Id,
                AssignedByUserId = adminUser.Id,
                AssignedAt     = DateTime.UtcNow
            },
            new UserDepartment
            {
                UserId         = employeeUser.Id,
                DepartmentId   = hr.Id,
                AssignedByUserId = adminUser.Id,
                AssignedAt     = DateTime.UtcNow
            },
        };

        await context.UserDepartments.AddRangeAsync(userDepartments);
        await context.SaveChangesAsync();
    }

    private static async Task SeedEmployeeProfiles(AppDbContext context)
    {
        if (context.EmployeeProfiles.Any()) return;

        var adminUser = context.Users.FirstOrDefault(u => u.Email == "admin@annualleave.com");
        var manager1 = context.Users.FirstOrDefault(u => u.Email == "manager1@annualleave.com");
        var manager2 = context.Users.FirstOrDefault(u => u.Email == "manager2@annualleave.com");
        var emp1a = context.Users.FirstOrDefault(u => u.Email == "employee1a@annualleave.com");
        var emp1b = context.Users.FirstOrDefault(u => u.Email == "employee1b@annualleave.com");
        var emp1c = context.Users.FirstOrDefault(u => u.Email == "employee1c@annualleave.com");
        var emp1d = context.Users.FirstOrDefault(u => u.Email == "employee1d@annualleave.com");
        var emp2a = context.Users.FirstOrDefault(u => u.Email == "employee2a@annualleave.com");
        var emp2b = context.Users.FirstOrDefault(u => u.Email == "employee2b@annualleave.com");
        var emp2c = context.Users.FirstOrDefault(u => u.Email == "employee2c@annualleave.com");
        var emp2d = context.Users.FirstOrDefault(u => u.Email == "employee2d@annualleave.com");

        var engineering = context.Departments.FirstOrDefault(d => d.Code == "ENG");
        var hr = context.Departments.FirstOrDefault(d => d.Code == "HR");
        var finance = context.Departments.FirstOrDefault(d => d.Code == "FIN");
        if (engineering is null || hr is null || finance is null) return;

        // Admin profile — no manager (top of hierarchy)
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

        // Manager 1 (Engineering)
        var manager1Profile = new EmployeeProfile
        {
            Id = Guid.NewGuid().ToString(),
            UserId = manager1.Id,
            DepartmentId = engineering.Id,
            ManagerId = adminProfile.Id,
            JobTitle = "Engineering Team Lead",
            AnnualLeaveEntitlement = 20,
            CreatedAt = DateTime.UtcNow
        };

        // Manager 2 (Finance)
        var manager2Profile = new EmployeeProfile
        {
            Id = Guid.NewGuid().ToString(),
            UserId = manager2.Id,
            DepartmentId = finance.Id,
            ManagerId = adminProfile.Id,
            JobTitle = "Finance Team Lead",
            AnnualLeaveEntitlement = 20,
            CreatedAt = DateTime.UtcNow
        };

        // Employees under Manager 1 (Engineering)
        var emp1Profiles = new[]
        {
            new EmployeeProfile { Id = Guid.NewGuid().ToString(), UserId = emp1a.Id, DepartmentId = engineering.Id, ManagerId = manager1Profile.Id, JobTitle = "Engineer", AnnualLeaveEntitlement = 20, CreatedAt = DateTime.UtcNow },
            new EmployeeProfile { Id = Guid.NewGuid().ToString(), UserId = emp1b.Id, DepartmentId = engineering.Id, ManagerId = manager1Profile.Id, JobTitle = "Engineer", AnnualLeaveEntitlement = 20, CreatedAt = DateTime.UtcNow },
            new EmployeeProfile { Id = Guid.NewGuid().ToString(), UserId = emp1c.Id, DepartmentId = engineering.Id, ManagerId = manager1Profile.Id, JobTitle = "Engineer", AnnualLeaveEntitlement = 20, CreatedAt = DateTime.UtcNow },
            new EmployeeProfile { Id = Guid.NewGuid().ToString(), UserId = emp1d.Id, DepartmentId = engineering.Id, ManagerId = manager1Profile.Id, JobTitle = "Engineer", AnnualLeaveEntitlement = 20, CreatedAt = DateTime.UtcNow },
        };

        // Employees under Manager 2 (Finance)
        var emp2Profiles = new[]
        {
            new EmployeeProfile { Id = Guid.NewGuid().ToString(), UserId = emp2a.Id, DepartmentId = finance.Id, ManagerId = manager2Profile.Id, JobTitle = "Accountant", AnnualLeaveEntitlement = 20, CreatedAt = DateTime.UtcNow },
            new EmployeeProfile { Id = Guid.NewGuid().ToString(), UserId = emp2b.Id, DepartmentId = finance.Id, ManagerId = manager2Profile.Id, JobTitle = "Accountant", AnnualLeaveEntitlement = 20, CreatedAt = DateTime.UtcNow },
            new EmployeeProfile { Id = Guid.NewGuid().ToString(), UserId = emp2c.Id, DepartmentId = finance.Id, ManagerId = manager2Profile.Id, JobTitle = "Accountant", AnnualLeaveEntitlement = 20, CreatedAt = DateTime.UtcNow },
            new EmployeeProfile { Id = Guid.NewGuid().ToString(), UserId = emp2d.Id, DepartmentId = finance.Id, ManagerId = manager2Profile.Id, JobTitle = "Accountant", AnnualLeaveEntitlement = 20, CreatedAt = DateTime.UtcNow },
        };

        await context.EmployeeProfiles.AddRangeAsync(adminProfile, manager1Profile, manager2Profile);
        await context.EmployeeProfiles.AddRangeAsync(emp1Profiles);
        await context.EmployeeProfiles.AddRangeAsync(emp2Profiles);
        await context.SaveChangesAsync();

        var profiles = new List<EmployeeProfile> { adminProfile, manager1Profile, manager2Profile };
        profiles.AddRange(emp1Profiles);
        profiles.AddRange(emp2Profiles);
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


