using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Domain;

namespace Persistence;

public class AppDbContext : IdentityDbContext<
    User,
    Role,
    string,
    IdentityUserClaim<string>,
    UserRole,
    IdentityUserLogin<string>,
    IdentityRoleClaim<string>,
    IdentityUserToken<string>>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<AppSettings> AppSettings { get; set; }
    public DbSet<AnnualLeave> AnnualLeaves { get; set; }
    public DbSet<LeaveType> LeaveTypes { get; set; }
    public DbSet<Department> Departments { get; set; }
    public DbSet<UserDepartment> UserDepartments { get; set; }
    public DbSet<EmployeeProfile> EmployeeProfiles { get; set; }
    public DbSet<LeaveStatusHistory> LeaveStatusHistories { get; set; }
    public DbSet<Project> Projects { get; set; }
    public DbSet<Timesheet> Timesheets { get; set; }
    public DbSet<TimesheetEntry> TimesheetEntries { get; set; }
    public DbSet<TimesheetStatusHistory> TimesheetStatusHistories { get; set; }
    public DbSet<AttendanceEvent> AttendanceEvents { get; set; }
    public DbSet<PublicHoliday> PublicHolidays { get; set; }
    public DbSet<ProjectActivityType> ProjectActivityTypes { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<TimesheetStatusHistory>(entity =>
        {
            entity.Property(e => e.Id)
                .HasMaxLength(450)
                .IsRequired();

            entity.Property(e => e.TimesheetId)
                                .HasMaxLength(450)
                                .IsRequired();

            entity.Property(e => e.ChangedByUserId)
                                .HasMaxLength(450)
                                .IsRequired();

            entity.Property(e => e.FromStatus)
                                .IsRequired();

            entity.Property(e => e.ToStatus)
                                .IsRequired();

            entity.Property(e => e.Comment)
                                .HasMaxLength(500);

            entity.Property(e => e.ChangedAt)
                                .IsRequired();

            entity.HasOne(e => e.Timesheet)
                                .WithMany(t => t.StatusHistory)
                                .HasForeignKey(e => e.TimesheetId)
                                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ChangedByUser)
                                .WithMany()
                                .HasForeignKey(e => e.ChangedByUserId)
                                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.TimesheetId);
        });

        builder.Entity<TimesheetEntry>(entity =>
        {
            entity.Property(e => e.Id)
                .HasMaxLength(450)
                .IsRequired();

            entity.Property(e => e.TimesheetId)
                .HasMaxLength(450)
                .IsRequired();

            entity.Property(e => e.ProjectId)
                .IsRequired();

            entity.Property(e => e.Date)
                .IsRequired();

            entity.Property(e => e.HoursWorked)
                .HasColumnType("decimal(4,2)")
                .IsRequired();

            entity.Property(e => e.Notes)
                .HasMaxLength(300);

            entity.HasOne(e => e.Timesheet)
                .WithMany(t => t.Entries)
                .HasForeignKey(e => e.TimesheetId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Project)
                .WithMany(p => p.TimesheetEntries)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ActivityType)
                .WithMany()
                .HasForeignKey(e => e.ActivityTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.TimesheetId);
        });

        builder.Entity<Timesheet>(entity =>
        {
            entity.Property(t => t.Id).HasMaxLength(450).IsRequired();
            entity.Property(t => t.EmployeeId).HasMaxLength(450).IsRequired();
            entity.Property(t => t.DepartmentId).IsRequired();
            entity.Property(t => t.PeriodStart).IsRequired();
            entity.Property(t => t.PeriodEnd).IsRequired();
            entity.Property(t => t.TotalHours).HasColumnType("decimal(5,2)").IsRequired();
            entity.Property(t => t.Status).IsRequired();
            entity.Property(t => t.ApproverId).HasMaxLength(450);
            entity.Property(t => t.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()").IsRequired();
            entity.HasIndex(t => t.EmployeeId);
            entity.HasIndex(t => t.DepartmentId);
            entity.HasOne(t => t.Employee).WithMany(e => e.Timesheets).HasForeignKey(t => t.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(t => t.Department).WithMany(d => d.Timesheets).HasForeignKey(t => t.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(t => t.Approver).WithMany(u => u.ApprovedTimesheets).HasForeignKey(t => t.ApproverId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<UserRole>(entity =>
        {
            entity.HasKey(ur => new { ur.UserId, ur.RoleId });
            entity.HasOne(ur => ur.User).WithMany(u => u.UserRoles).HasForeignKey(ur => ur.UserId).IsRequired();
            entity.HasOne(ur => ur.Role).WithMany(r => r.UserRoles).HasForeignKey(ur => ur.RoleId).IsRequired();
            entity.ToTable("AspNetUserRoles");
        });

        builder.Entity<UserDepartment>(entity =>
        {
            entity.HasKey(ud => new { ud.UserId, ud.DepartmentId });
            entity.HasOne(ud => ud.User).WithMany(u => u.UserDepartments).HasForeignKey(ud => ud.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(ud => ud.AssignedByUser).WithMany(u => u.AssignedUserDepartments).HasForeignKey(ud => ud.AssignedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(ud => ud.Department).WithMany(d => d.UserDepartments).HasForeignKey(ud => ud.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AnnualLeave>(entity =>
        {
            entity.HasOne(al => al.Employee)
                .WithMany()
                .HasForeignKey(al => al.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(al => al.ApprovedBy)
                .WithMany()
                .HasForeignKey(al => al.ApprovedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AttendanceEvent>(entity =>
        {
            entity.Property(e => e.Id).HasMaxLength(450).IsRequired();
            entity.Property(e => e.EmployeeId).HasMaxLength(450).IsRequired();
            entity.Property(e => e.At).IsRequired();
            entity.Property(e => e.Type).IsRequired();
            entity.HasIndex(e => new { e.EmployeeId, e.At });
            entity.HasOne(e => e.Employee)
                .WithMany()
                .HasForeignKey(e => e.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PublicHoliday>(entity =>
        {
            entity.Property(e => e.CountryCode).HasMaxLength(2).IsRequired();
            entity.Property(e => e.LocalName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.EnglishName).HasMaxLength(200).IsRequired();
            entity.HasIndex(e => new { e.CountryCode, e.Year });
            entity.HasIndex(e => new { e.CountryCode, e.Date }).IsUnique();
        });

        builder.Entity<LeaveType>(entity =>
        {
            entity.Property(lt => lt.Name).IsRequired().HasMaxLength(100);
            entity.Property(lt => lt.Icon).HasMaxLength(16).HasDefaultValue("🏷️");
            entity.Property(lt => lt.ColorKey).HasMaxLength(30).HasDefaultValue("default");
            entity.Property(lt => lt.Description).HasMaxLength(300).HasDefaultValue(string.Empty);
            entity.Property(lt => lt.Paid).HasDefaultValue(true);
            entity.Property(lt => lt.AttachmentPolicy).HasDefaultValue(AttachmentPolicy.None);
            entity.Property(lt => lt.DefaultAllowance).HasDefaultValue(0);
            entity.Property(lt => lt.AllowanceUnit).HasMaxLength(30).HasDefaultValue("days/year");
            entity.Property(lt => lt.AccrualNotes).HasMaxLength(250).HasDefaultValue(string.Empty);
            entity.Property(lt => lt.MinNoticeDays).HasDefaultValue(0);
            entity.Property(lt => lt.MaxConsecutiveDays).HasDefaultValue(0);
            entity.Property(lt => lt.HalfDayAllowed).HasDefaultValue(false);
            entity.Property(lt => lt.EligibilityNotes).HasMaxLength(250).HasDefaultValue("All employees");
            entity.Property(lt => lt.EligibilityScope).HasDefaultValue(EligibilityScope.All);
        });

        builder.Entity<ProjectActivityType>(entity =>
        {
            entity.Property(a => a.Name).IsRequired().HasMaxLength(100);
            entity.Property(a => a.Description).HasMaxLength(300).HasDefaultValue(string.Empty);
            entity.Property(a => a.Icon).HasMaxLength(16).HasDefaultValue("🏷️");
            entity.Property(a => a.ColorKey).HasMaxLength(30).HasDefaultValue("default");
            entity.Property(a => a.IsActive).HasDefaultValue(true);
            entity.HasIndex(a => a.Name).IsUnique();
        });

        builder.Entity<Project>(entity =>
        {
            entity.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(p => p.Code)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(p => p.IsActive)
                .HasDefaultValue(true)
                .IsRequired();

            entity.Property(p => p.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .IsRequired();

            entity.HasIndex(p => p.Name)
                .IsUnique();

            entity.HasIndex(p => p.Code)
                .IsUnique();

            entity.HasOne(p => p.Department)
                .WithMany(d => d.Projects)
                .HasForeignKey(p => p.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(p => p.Description).HasMaxLength(500).HasDefaultValue(string.Empty);
            entity.Property(p => p.Status).HasDefaultValue(ProjectStatus.Active);
            entity.Property(p => p.ColorKey).HasMaxLength(8).HasDefaultValue("p1");
            entity.Property(p => p.TargetWeeklyHours).HasDefaultValue(0);
            entity.Property(p => p.TargetMonthlyHours).HasDefaultValue(0);
            entity.Property(p => p.OwnerId).HasMaxLength(450);

            entity.HasOne(p => p.Owner)
                .WithMany()
                .HasForeignKey(p => p.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(p => p.IsDeleted).HasDefaultValue(false).IsRequired();
            entity.HasQueryFilter(p => !p.IsDeleted);
        });

        builder.Entity<EmployeeProfile>(entity =>
        {
            entity.Property(e => e.IsDeleted).HasDefaultValue(false).IsRequired();
            entity.HasQueryFilter(e => !e.IsDeleted);
        });

        builder.Entity<AuditLog>(entity =>
        {
            entity.Property(a => a.EntityName).IsRequired().HasMaxLength(100);
            entity.Property(a => a.EntityId).IsRequired().HasMaxLength(450);
            entity.Property(a => a.Action).IsRequired();
            entity.Property(a => a.Changes).IsRequired();
            entity.Property(a => a.UserId).HasMaxLength(450);
            entity.Property(a => a.Timestamp).HasDefaultValueSql("SYSUTCDATETIME()").IsRequired();
            entity.HasIndex(a => new { a.EntityName, a.EntityId });
            entity.HasIndex(a => a.Timestamp);
            entity.HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
