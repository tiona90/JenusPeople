using Application.TimesheetStatusHistories.Queries;
using Domain;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Verifies opt-in pagination on the admin list handlers: filtering/ordering/paging
/// all run in SQL, Total reflects the full filtered set, and omitting page/pageSize
/// returns every row (unchanged, backward-compatible behaviour).
/// </summary>
public class PaginationTests
{
    private static void SeedHistories(AppDbContext db, int count)
    {
        db.Users.Add(new User { Id = "u", UserName = "u", Email = "u@test.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "p", UserId = "u", DepartmentId = 1 });
        db.Timesheets.Add(new Timesheet { Id = "ts", EmployeeProfileId = "p", DepartmentId = 1, PeriodStart = new DateTime(2024, 1, 1), PeriodEnd = new DateTime(2024, 1, 7), Status = TimesheetStatus.Approved });
        for (var i = 0; i < count; i++)
        {
            db.TimesheetStatusHistories.Add(new TimesheetStatusHistory
            {
                Id = $"h{i:D2}",
                TimesheetId = "ts",
                ChangedByUserId = "u",
                FromStatus = 1,
                ToStatus = 2,
                ChangedAt = new DateTime(2024, 1, 1).AddDays(i), // ascending → newest is last
            });
        }
        db.SaveChanges();
    }

    private static GetTimesheetStatusHistoryList.Query AdminQuery(int? page, int? pageSize) => new()
    {
        RequestingUserId = "admin",
        IsAdmin = true,
        Page = page,
        PageSize = pageSize,
    };

    [Fact]
    public async Task Unpaged_request_returns_all_rows_and_no_page_metadata()
    {
        using var db = TestDb.Create();
        SeedHistories(db, 5);

        var result = await new GetTimesheetStatusHistoryList.Handler(db).Handle(AdminQuery(null, null), CancellationToken.None);

        Assert.Equal(5, result.Items.Count);
        Assert.Equal(5, result.Total);
        Assert.Null(result.Page);
        Assert.Null(result.PageSize);
    }

    [Fact]
    public async Task First_page_returns_page_size_rows_with_total_of_full_set()
    {
        using var db = TestDb.Create();
        SeedHistories(db, 5);

        var result = await new GetTimesheetStatusHistoryList.Handler(db).Handle(AdminQuery(1, 2), CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(5, result.Total);      // total is the full filtered count, not the page
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.PageSize);
        // OrderByDescending(ChangedAt): newest first → the last-seeded history (h04).
        Assert.Equal("h04", result.Items[0].Id);
    }

    [Fact]
    public async Task Last_partial_page_returns_the_remainder()
    {
        using var db = TestDb.Create();
        SeedHistories(db, 5);

        var result = await new GetTimesheetStatusHistoryList.Handler(db).Handle(AdminQuery(3, 2), CancellationToken.None);

        Assert.Single(result.Items); // 5 rows, pages of 2 → page 3 has 1
        Assert.Equal(5, result.Total);
    }
}
