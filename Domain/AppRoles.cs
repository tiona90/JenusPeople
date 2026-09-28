using System.Security.Claims;

namespace Domain;

public static class AppRoles
{
    public const string Employee = "Employee";
    public const string Manager = "Manager";
    public const string SystemAdministrator = "System Administrator";

    /// <summary>
    /// The administrator's <b>reach</b> without the administrator's <b>hand on the
    /// configuration</b> — and, since department scope arrived, a reach bounded by the
    /// departments a System Administrator assigns them (<c>UserDepartment</c> rows).
    /// Within those departments an HR Administrator runs Leave Management, Attendance
    /// and Timesheets and files leave on somebody's behalf; they carry the same
    /// role-scoped rules as a System Administrator (no department, gender or employment
    /// start date of their own, no coverage on their own leave, excluded from
    /// attendance). What they cannot touch is system administration: Users, Departments,
    /// Projects and their catalogues, Leave Types, Organization, Notification Settings
    /// and Data Maintenance.
    ///
    /// So there are three questions, and a gate has to ask the right one:
    /// <list type="bullet">
    ///   <item><b>May open the company-wide pages</b> — <see cref="Administrators"/> /
    ///   <see cref="AdministratorRoles"/> / <see cref="IsAdministrator(string?)"/>. Both roles.
    ///   Authorization only; never a data filter.</item>
    ///   <item><b>Unscoped data</b> — <see cref="IsSystemAdministrator"/>. That role alone.
    ///   <b>Scoped data</b> — <see cref="IsDepartmentScoped"/>, a Manager or an HR
    ///   Administrator, resolved through <c>ManagerAccessScopeResolver</c>.</item>
    ///   <item><b>System administration</b> — <see cref="SystemAdministrator"/> by name,
    ///   on the configuration controllers' write actions and the <c>EmployeeProfileUpdate</c>
    ///   policy. That role alone.</item>
    /// </list>
    /// </summary>
    public const string HrAdministrator = "HR Administrator";

    public static readonly string[] All = { SystemAdministrator, HrAdministrator, Manager, Employee };

    /// <summary>
    /// The two administrator roles — who may open the company-wide pages and who carries the administrator rules. Not a data-scope: an HR Administrator's data is bounded by their assigned departments. Query with
    /// <c>Contains</c> — and keep it typed as a list, not an array: on C# 14 an array's
    /// <c>Contains</c> inside an EF expression binds to the <c>ReadOnlySpan</c> overload,
    /// which the query evaluator cannot interpret, and every query that reads it throws
    /// at runtime.
    /// </summary>
    public static readonly IReadOnlyList<string> Administrators = new[] { SystemAdministrator, HrAdministrator };

    /// <summary>
    /// <see cref="Administrators"/> as the comma-separated list <c>[Authorize(Roles = ...)]</c>
    /// takes. A <c>const</c> so it can sit in an attribute. For the reach, not the
    /// configuration — a write on a catalogue names <see cref="SystemAdministrator"/>.
    /// </summary>
    public const string AdministratorRoles = SystemAdministrator + "," + HrAdministrator;

    /// <summary>
    /// Who decides leave and time for other people: an HR Administrator company-wide,
    /// a Manager within their department. For <c>[Authorize(Roles = ...)]</c> on the
    /// approve/reject actions. The System Administrator is deliberately absent — they
    /// configure the workspace and neither file nor decide leave — which is also why
    /// the leave controllers ask <see cref="IsHrAdministrator"/>, not
    /// <see cref="IsAdministrator(ClaimsPrincipal)"/>, before treating a caller as
    /// acting on somebody's behalf. Reads keep the wider gate: the Users and
    /// Departments panels quote leave and attendance figures.
    /// </summary>
    public const string LeaveAndTimeDecisionRoles = HrAdministrator + "," + Manager;

    /// <summary>
    /// Who works in Leave &amp; Time: everyone but the System Administrator, who
    /// configures the workspace. Mirrors <c>LEAVE_AND_TIME_ROLES</c> in
    /// <c>client/src/lib/roles.ts</c>. Comma-joined for <c>[Authorize(Roles = ...)]</c>.
    /// </summary>
    public const string LeaveAndTimeRoles = HrAdministrator + "," + Manager + "," + Employee;

    /// <summary>Whether the signed-in principal is the HR Administrator — the one administrator who runs Leave &amp; Time.</summary>
    public static bool IsHrAdministrator(this ClaimsPrincipal user) => user.IsInRole(HrAdministrator);

    /// <summary>Whether <paramref name="role"/> is one of the administrator roles. Case-insensitive, like Identity.</summary>
    public static bool IsAdministrator(string? role) =>
        role is not null && Administrators.Contains(role.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the signed-in principal holds any administrator role.</summary>
    public static bool IsAdministrator(this ClaimsPrincipal user) =>
        Administrators.Any(user.IsInRole);

    /// <summary>
    /// The roles whose reach is a set of departments rather than the whole company:
    /// a Manager (their own department) and an HR Administrator (the departments
    /// assigned to them). Both resolve through <c>ManagerAccessScopeResolver</c>.
    /// Typed as a list for the same EF reason as <see cref="Administrators"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> DepartmentScopedRoles = new[] { Manager, HrAdministrator };

    /// <summary>
    /// Whether the signed-in principal sees every department unfiltered. System
    /// Administrator alone — this is what a query's <c>IsAdmin</c> flag now means.
    /// </summary>
    public static bool IsSystemAdministrator(this ClaimsPrincipal user) => user.IsInRole(SystemAdministrator);

    /// <summary>
    /// Whether the signed-in principal's reach is a department set — a Manager or an
    /// HR Administrator. This is what a query's <c>IsManager</c> flag now means.
    /// </summary>
    public static bool IsDepartmentScoped(this ClaimsPrincipal user) => DepartmentScopedRoles.Any(user.IsInRole);
}
