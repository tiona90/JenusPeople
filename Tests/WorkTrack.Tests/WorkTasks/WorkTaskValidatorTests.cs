using Application.WorkTasks.DTOs;
using Application.WorkTasks.Validators;
using Domain;
using Xunit;

namespace WorkTrack.Tests.WorkTasks;

public class WorkTaskValidatorTests
{
    private static bool Valid(UpsertWorkTaskRequest r) => new UpsertWorkTaskRequestValidator().Validate(r).IsValid;

    private static UpsertWorkTaskRequest Ok() => new() { Title = "Do it", DepartmentId = 1, AssigneeId = "u" };

    [Fact] public void A_complete_request_passes() => Assert.True(Valid(Ok()));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_title_is_refused(string title) => Assert.False(Valid(new UpsertWorkTaskRequest { Title = title, DepartmentId = 1, AssigneeId = "u" }));

    [Fact] public void A_title_over_200_is_refused() => Assert.False(Valid(new UpsertWorkTaskRequest { Title = new string('x', 201), DepartmentId = 1, AssigneeId = "u" }));

    [Fact] public void A_description_over_2000_is_refused()
    {
        var r = Ok(); r.Description = new string('x', 2001);
        Assert.False(Valid(r));
    }

    [Fact] public void A_missing_department_or_assignee_is_refused()
    {
        Assert.False(Valid(new UpsertWorkTaskRequest { Title = "x", DepartmentId = 0, AssigneeId = "u" }));
        Assert.False(Valid(new UpsertWorkTaskRequest { Title = "x", DepartmentId = 1, AssigneeId = "" }));
    }

    [Fact] public void An_unknown_priority_is_refused()
    {
        var r = Ok(); r.Priority = (WorkTaskPriority)9;
        Assert.False(Valid(r));
    }
}
