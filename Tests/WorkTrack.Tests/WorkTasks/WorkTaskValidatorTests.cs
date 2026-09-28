using Application.WorkTasks.DTOs;
using Application.WorkTasks.Validators;
using Domain;
using Xunit;

namespace WorkTrack.Tests.WorkTasks;

public class WorkTaskValidatorTests
{
    private static bool Valid(UpsertWorkTaskRequest r) => new UpsertWorkTaskRequestValidator().Validate(r).IsValid;

    private static UpsertWorkTaskRequest Ok() => new() { Title = "Do it", DepartmentId = 1, ProjectId = 10, AssigneeIds = ["u"], IsBillable = false };

    [Fact] public void A_complete_request_passes() => Assert.True(Valid(Ok()));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_title_is_refused(string title) => Assert.False(Valid(new UpsertWorkTaskRequest { Title = title, DepartmentId = 1, ProjectId = 10, AssigneeIds = ["u"], IsBillable = true }));

    [Fact] public void A_title_over_200_is_refused() => Assert.False(Valid(new UpsertWorkTaskRequest { Title = new string('x', 201), DepartmentId = 1, ProjectId = 10, AssigneeIds = ["u"], IsBillable = true }));

    [Fact] public void A_description_over_2000_is_refused()
    {
        var r = Ok(); r.Description = new string('x', 2001);
        Assert.False(Valid(r));
    }

    [Fact] public void A_missing_department_or_assignee_is_refused()
    {
        Assert.False(Valid(new UpsertWorkTaskRequest { Title = "x", DepartmentId = 0, ProjectId = 10, AssigneeIds = ["u"], IsBillable = true }));
        Assert.False(Valid(new UpsertWorkTaskRequest { Title = "x", DepartmentId = 1, ProjectId = 10, AssigneeIds = [], IsBillable = true }));
    }

    [Fact] public void An_unknown_priority_is_refused()
    {
        var r = Ok(); r.Priority = (WorkTaskPriority)9;
        Assert.False(Valid(r));
    }

    [Fact] public void A_missing_project_is_refused()
    {
        var r = Ok(); r.ProjectId = null;
        Assert.False(Valid(r));
    }

    [Fact] public void Several_assignees_pass()
    {
        var r = Ok(); r.AssigneeIds = ["a", "b", "c"];
        Assert.True(Valid(r));
    }

    [Fact] public void The_same_assignee_twice_is_refused()
    {
        var r = Ok(); r.AssigneeIds = ["a", "a"];
        Assert.False(Valid(r));
    }

    [Fact] public void A_blank_assignee_id_is_refused()
    {
        var r = Ok(); r.AssigneeIds = ["a", " "];
        Assert.False(Valid(r));
    }

    [Fact] public void More_than_twenty_assignees_are_refused()
    {
        var r = Ok(); r.AssigneeIds = Enumerable.Range(1, 21).Select(i => $"u{i}").ToList();
        Assert.False(Valid(r));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(1, true)]
    [InlineData(9999, true)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(10000, false)]
    public void Target_hours_are_optional_but_must_be_sensible(int? hours, bool valid)
    {
        var r = Ok(); r.TargetHours = hours;
        Assert.Equal(valid, Valid(r));
    }

    [Fact] public void Billable_or_not_has_to_be_answered()
    {
        var r = Ok(); r.IsBillable = null;
        Assert.False(Valid(r));
    }
}
