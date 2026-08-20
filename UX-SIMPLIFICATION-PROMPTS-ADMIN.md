# UX simplification prompts — Admin role

Ten small, independent Claude Code prompts from the 2026-08-20 admin-role UX walkthrough
(screenshots reviewed, no code changed yet). Companion to `UX-SIMPLIFICATION-PROMPTS.md`
(employee) and `UX-SIMPLIFICATION-PROMPTS-MANAGER.md` (manager). Each is scoped to one
finding and safe to run on its own — hand them to separate sessions/subagents or work
through them one at a time.

Walked as `admin@annualleave.com` across all eleven admin destinations. The dev database
holds 13 users, 12 of which have an `EmployeeProfile` (`manager1@annualleave.com` has
none), and 4 pending leave requests — two belonging to users with no profile, which is
what several of these findings turn on.

## 1. Admin dashboard — the hero states the headcount twice, with two different numbers

```
The admin dashboard hero in client/src/components/annual-leave/DashboardHome.tsx (AdminDashboard, from ~line 676) contradicts itself in adjacent lines: the subtitle reads "2 of 12 employees are working right now" (from company.total, line 728) while the meta stat directly beneath reads "Total users · 11 active" (totalUsers, line 697, which filters adminUsers to those holding an Employee or Manager role and so excludes the two Admin-only accounts). The Users panel calls the same population 13 and the Departments panel calls it 12, so an admin sees three totals for one company across three pages. Pick one number for the hero and label it for what it actually counts — either drop the "Total users" meta stat because the sentence above it already states the figure, or keep it and make both sides use the same source. Note the Users panel already has the right vocabulary for this distinction: it shows "TOTAL USERS 13" beside "WITH PROFILE 12 of 13 users have an employee profile". Keep it a counting/labelling fix, not a new stats service. Verify by logging in as admin@annualleave.com and confirming the hero sentence and the stat beside it agree.
```

## 2. Admin dashboard — the "Active issues" tile answers three different questions at once

```
In client/src/components/annual-leave/DashboardHome.tsx, the "Active issues" Gauge (line 746) is built from three unrelated quantities: the big number counts only issues with severity 'danger' (renders 2), the sub-label shows company.out ("10 not checked in"), and the progress bar is issues.length * 20 (renders 80%). Three denominators in one tile, none of which explain the others. Worse, the "Today's issues" card it summarises (TodaysIssuesCard, fed at line 754) renders a green "No unusual overtime · All employees within healthy hour ranges" row — a success message counted in issues.length and therefore inflating the bar. Make the tile consistent: count the same set of issues in the number and the bar, and either exclude non-issue rows from that set or state in the sub-label what the number counts. Keep it to the Gauge's three props plus, if needed, one filter on the issues array — no new component. Verify as admin@annualleave.com that the number, the bar and the issues card below tell the same story.
```

## 3. Admin dashboard — the "Administration" grid is a second copy of the sidebar

```
In client/src/components/annual-leave/DashboardHome.tsx, the AdminDashboard's "Administration" quick-actions grid (lines 758-767) offers eight tiles — All leave, All timesheets, Company attendance, Users, Departments, Leave types, Projects, Leave year — and every single one of them is already a permanent entry in the admin sidebar (client/src/components/layout/Sidebar.tsx lines 199-216). Unlike the employee and manager dashboards, where quick actions surface things nav doesn't offer (apply for leave, start this week's timesheet), nothing in this grid is unavailable one click away in the left rail. Remove the "Administration" ActionCard entirely and let the Recent activity card that shares its row take the full width; the pending counts it carried are already on the "Pending approvals" gauge above. Don't touch the employee or manager quick actions. Verify as admin@annualleave.com that the dashboard still reaches every admin section from the sidebar and that Recent activity fills the row cleanly.
```

## 4. All Leave — one screen shows four different pending counts

```
The admin All Leave page (client/src/components/annual-leave/AllLeaveAdminPage.tsx) states the number of pending requests four times in one viewport, and gets four different answers: the "AWAITING REVIEW" stat card says 4, the "Pending" tab badge says 4, the "Leave by Department" panel says none for Engineering plus 2 for Finance, and the list header below reads "AWAITING REVIEW · 1 REQUEST" with a single row rendered. Two separate causes, both worth fixing here: (a) the department rollup silently drops requests whose owner has no EmployeeProfile — two of the four pending requests belong to admin@annualleave.com and manager1@annualleave.com, neither of which has a profile row, so they are counted in the header but belong to no department; (b) the list is filtered by a date range that defaults to "This month" (dateRange state, line 105; applied at lines 184-205) while every count above it is unfiltered, and three of the four pending requests start in September or October. Make the counts and the list agree — either have the stat cards and tab badges respect the active filters, or default the range to one that shows everything pending — and give unattributed requests a visible bucket in the department rollup rather than dropping them. Keep it a counting/filtering fix, not a redesign of the page. Verify as admin@annualleave.com that every "pending" number on the page matches the number of rows you can actually see.
```

## 5. All Leave — an almost-empty calendar fills the screen before the review queue

```
On the admin All Leave page (client/src/components/annual-leave/AllLeaveAdminPage.tsx), the first screenful is four stat cards followed by a full-month "August 2026 · Leave Calendar" grid (the Heatmap rendered at lines 401-410) roughly a thousand pixels tall, of which exactly two cells carry a booking. The review queue — the reason the page exists, and the only part with Approve/Reject buttons — starts below the fold, under the calendar and a department bar chart. Move the request list above the calendar, or collapse the calendar behind a toggle that remembers its state, so the queue is what loads first. The calendar's "Click a request below to see details" caption already tells you the list is the primary surface. Keep it a reordering (or one collapse control), not a new layout system. Verify as admin@annualleave.com that the pending request rows are visible without scrolling at 1400px tall.
```

## 6. Departments — every department is flagged, so the flag says nothing

```
In client/src/components/admin/DepartmentsPanel.tsx, all five departments render a "Needs attention" chip, the stat card reads "DEPARTMENTS 5 · 0 healthy · 0 inactive" (a breakdown that accounts for none of the five), and the status filter offers "Active (0)" and "Inactive (0)" — two options that can never return a row while the flag applies to everything. Three of the five are flagged only for being empty, and each says so twice in stacked banners: a red "⚠️ Manager position vacant" directly above an amber "⚠ No manager assigned · Approvals are routed to Admin". Do three things: raise the bar for "needs attention" so a department with no members and no manager is described as not set up yet rather than flagged as a problem; collapse the two manager-vacant banners into the single amber one, which is the one that says what happens as a result; and make the stat card's breakdown describe the same buckets the filter offers. Note the dashboard calls these same five "Departments · 5 active", so whatever "active" means it should mean the same thing in both places. Keep it to the status derivation and the banner markup. Verify as admin@annualleave.com that Engineering and Finance read differently from the three empty departments.
```

## 7. Company Attendance — it repeats the dashboard, then contradicts it

```
client/src/components/attendance/CompanyAttendancePage.tsx renders a "⚠️ Today's Issues" block and a "Recent Activity" list that are the same data, in the same order, as the "Today's issues" and "Recent activity" cards an admin has just scrolled past on the dashboard (client/src/components/annual-leave/DashboardHome.tsx, lines 753-757 and 759). Where they differ is worse than where they repeat: this page marks everyone who has not checked in with a red 🔴 and the word "flagged", while the dashboard lists the same people with a green dot, and its "Not Checked In" stat card sub-labels them "requires follow-up". Pick one reading of not-checked-in and use it in both places — the dashboard's neutral treatment is the honest one at 13:00, since the app cannot tell an absence from a late start. Then drop the duplicated Today's Issues block from this page, which already has four stat cards and a full department table saying the same thing, or drop it from the dashboard and link across. Keep it to the status colour, the sub-label copy, and removing one block. Verify as admin@annualleave.com that the same person reads the same way on both pages.
```

## 8. Users — the row action buttons fall out of their row

```
In client/src/components/admin/AdminUsersPanel.tsx, each user row is a CSS grid declared with seven md columns ('24px 240px 110px 140px 130px 150px auto', line 670) but rendered with eight children: the checkbox, User, Role pill, Department, Status, Leave, Last active, and Actions (line 794). The eighth is auto-placed into an implicit second row at column one, so the ✏️/🗑 buttons render at the bottom-left of every unprotected card instead of the right-hand end of the row, straddling the card's edge and the one below it. Add the missing eighth column to gridTemplateColumns so Actions lands where its `justifyContent: 'flex-end'` intends. While in the same row, fix the "Last active" cell (lines 784-792): for anyone who has never been seen it renders the value "No activity" above the label "last active", so eleven of thirteen rows show a label with nothing to label — use one line for that case. Keep it to the grid template and that one cell. Verify as admin@annualleave.com that every row's edit and delete buttons sit at the right-hand end, inside the card.
```

## 9. Leave allowance is defined in three places and they disagree

```
The annual leave allowance has three separate sources and an admin can see two of them contradict each other in one session. AppSettings.DefaultAnnualEntitlement is 20 and surfaces on Leave Settings (client/src/components/admin/AppSettingsPanel.tsx) as "Default Annual Entitlement 20", with the carryover legend below spelling out "New balance = 5 + 20 = 25". LeaveTypes.DefaultAllowance for Annual Leave is 25 and surfaces on Leave Types (client/src/components/admin/LeaveTypesPanel.tsx) as "DEFAULT ALLOWANCE 25 days/year". EmployeeProfiles.AnnualLeaveEntitlement is 20 and drives the per-user bars on Users and the "0/20 used · 18 left after" figure on each All Leave row — which is shown even for a Sick Leave request, whose own type says 10 days/year. Decide which one is authoritative (the leave type's allowance is the only one that can vary per type, so it is the strongest candidate) and have the other surfaces read from it or state plainly that they are a fallback for types that don't set their own. At minimum, label the Leave Settings field so it doesn't read as the same number the Leave Types page shows, and make the balance on an All Leave row refer to the balance for that request's leave type. Keep it a sourcing fix, no schema migration. Verify as admin@annualleave.com that Leave Settings and Leave Types no longer state different annual allowances, and that the pending Sick Leave request is measured against the sick allowance.
```

## 10. Reminders & Notifications — it also holds org settings and a delete button

```
client/src/components/admin/OrgSettingsPanel.tsx puts three unrelated things on the page the sidebar calls "Reminders & Notifications": the reminder schedule the name promises, an "🏢 Organization Settings" block, and a "⚠️ Danger Zone" whose "Clear all approval history" button deletes thirty days of approval records irreversibly. The Organization Settings block overlaps the Leave Settings page directly — its "Financial year · When leave allocations reset · January 1 – December 31" control is the same concept Leave Settings calls "Leave Year Start Month", and they are backed by two different columns (AppSettings.FinancialYearStartMonth and AppSettings.LeaveYearStartMonth) that an admin can set to different values with nothing warning them. Move the org-wide settings that are already on Leave Settings out of this page (or move all of them here and leave Leave Settings to the leave year alone) so each setting has one home, and relocate the Danger Zone off a preferences page — a destructive, irreversible action does not belong under notification toggles. Keep it a move plus a de-duplication, not a settings rewrite. Verify as admin@annualleave.com that the leave/financial year is editable in exactly one place.
```

---

Full walkthrough with screenshots: https://claude.ai/code/artifact/afc7ad80-557a-425a-8f03-4749dd8ea005
