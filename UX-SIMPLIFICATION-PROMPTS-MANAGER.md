# UX simplification prompts — Manager role

Eight small, independent Claude Code prompts from the 2026-08-20 manager-role UX
walkthrough (screenshots reviewed, no code changed yet). Companion to
`UX-SIMPLIFICATION-PROMPTS.md`, which covers the employee role. Each is scoped to one
finding and safe to run on its own — hand them to separate sessions/subagents or work
through them one at a time.

Walked as `manager2@annualleave.com` (Finance, 5 reports). Note `manager1@annualleave.com`
has **no EmployeeProfile row** in the dev database, so its dashboard degrades to "No
employee profile found" with every panel zeroed — use manager2 to reproduce any of these.

## 1. Manager dashboard — the same pending count is stated five times

```
The manager dashboard in client/src/components/annual-leave/DashboardHome.tsx (ManagerDashboard, from ~line 357) states the same "3 items waiting" figure five times in one viewport: the hero subtitle ("Your team has 3 items waiting for review — 2 leave requests and 1 timesheet", built in buildManagerSummary around line 669), the hero meta stat "Approvals due · 3 items" (line 573), the Approval queue card's "3 waiting" badge, the Quick actions tiles ("Team leave · 2 pending", "Team timesheets · 1 pending"), and the topbar notification bell badge. Remove the "Approvals due" entry from the hero meta array — the sentence directly above it already says the same number in words, with the leave/timesheet split the stat lacks. Keep the other three hero stats (Team size, Working now, On leave), the queue badge and the bell. Verify by logging in as manager2@annualleave.com and confirming the hero shows three stats, the subtitle still names the total, and the Approval queue badge still reads the same count.
```

## 2. Manager dashboard — Quick actions duplicates the approval queue's own links

```
In client/src/components/annual-leave/DashboardHome.tsx, the ManagerDashboard's Quick actions grid (~line 599) offers "Team leave" and "Team timesheets" tiles, but the Approval queue card directly above already has "All leave" and "All timesheets" buttons pointing at the same two routes (onViewAllLeave / onViewAllTs, wired at lines 584-585), and both are also permanent sidebar entries. Drop those two tiles from the Quick actions array and keep only the two that are genuinely different actions — "My timesheet" and "Apply leave" (the manager acting as an employee, which nothing else on the page offers). Don't touch the employee or admin Quick actions. Verify as manager2@annualleave.com that Quick actions shows two tiles, and that "All leave" / "All timesheets" on the queue card still navigate correctly.
```

## 3. Manager dashboard — "This week's submissions" contradicts the queue above it

```
In client/src/components/annual-leave/DashboardHome.tsx, the teamSubmissions memo (~line 514) compares two different id spaces: it fills a `submitted` set with `t.employeeId` from the timesheets list (which carries the EmployeeProfile id) but then tests membership with `submitted.has(userId)` where `userId` comes from the employee profile's `userId` (the Identity user id). They never match, so every teammate falls into the "missing" list while `submitted.size` still counts the submissions — producing the contradiction visible on screen: "1 / 5 submitted" with "Employee 2C · No timesheets yet" listed as outstanding, while the Approval queue directly above shows Employee 2C's submitted 29.5h timesheet awaiting approval. Fix the comparison so both sides use the same identifier, and check the same confusion at line 378 (`t.employeeId !== user.id`, which is meant to keep the manager's own timesheet out of their approval queue). Keep it a targeted id fix, not a rewrite of the panel. Verify as manager2@annualleave.com that Employee 2C no longer appears under "NOT YET SUBMITTED" and the ratio matches the number of names listed.
```

## 4. Manager dashboard — the check-in chart silently hides afternoon check-ins

```
In client/src/components/annual-leave/DashboardHome.tsx, the "Check-in time per member · last 30 days" LineChart in TeamHealthCard (~line 1597) hard-clamps its y-axis to `min: 6 * 60, max: 12 * 60` (lines 1605-1606). Anyone who checks in after noon is plotted outside the visible range, so the chart renders a full axis grid and legend with no line at all and no explanation — reproducible today, where Employee 2A checked in at 15:59 and the chart appears blank. Derive the y-axis bounds from the actual data (padded to a sensible round hour) instead of hard-coding 06:00–12:00, keeping the existing `noCheckInData` empty state for when there's genuinely nothing to plot. Keep it a bounds calculation, not a new chart component. Verify as manager2@annualleave.com that Employee 2A's 15:59 check-in is visible on the line.
```

## 5. Team Attendance — reconcile the clocked hours against timesheet hours

```
client/src/components/attendance/TeamAttendancePage.tsx shows a "Weekly Attendance Log" where Employee 2C reads "0h 00m" for the current week, while Team Timesheets shows the same employee submitting 29.5 hrs for that same week — with nothing on the page explaining that one is physical clock-in time and the other is allocated work hours. Add a short one-line caption under the "Weekly Attendance Log" heading (~line 205), in the same spirit as the caption already added to the employee Attendance page's "This Week" card: something like "Time clocked in — separate from the hours your team logs on timesheets". While there, soften the "Not checked in" stat card's sub-label (line 174), which currently reads "follow up?" — a question posed as a statistic that reads as a nudge to chase people; plain copy like "no check-in today" is enough. Keep it to static copy, no new logic. Verify as manager2@annualleave.com that both read clearly next to their numbers.
```

## 6. Review dialogs — Approve and Reject swap positions between the two of them

```
The two manager review dialogs put the destructive action in opposite places. In client/src/components/annual-leave/TeamLeavePage.tsx the leave detail dialog's footer is Close · Approve · Reject, matching the table row's View · Approve · Reject; in client/src/components/timesheet/TeamTimesheetPage.tsx the timesheet detail dialog's footer is Close · Reject · Approve — so Reject sits exactly where Approve was a moment earlier, for a manager clicking through a queue of both kinds. Make the timesheet dialog match the leave dialog and the table rows (Approve before Reject). While in TeamTimesheetPage.tsx, sort the entry rows in that dialog ascending by date — they currently render newest-first (20, 19, 18, 17 Aug), which reads backwards for a week being reviewed. Keep both changes to ordering, no restyling. Verify as manager2@annualleave.com by opening View on the pending Employee 2C timesheet and on a pending leave request and confirming the button order matches and the entries run Mon→Fri.
```

## 7. Finish the neutral leave icons on the manager-facing surfaces

```
The sensitive leave types were switched from cartoon-face emoji to neutral MUI icons in ApplyLeavePage.tsx and MyLeavePage.tsx, but two files kept their own copies of the icon map and still render the old emoji — visible to a manager on the dashboard approval queue, which shows "🤒 2 days · 27 Aug – 28 Aug" for a sick-leave request. Lift one shared map into a small module (e.g. client/src/components/annual-leave/leave-icons.tsx, next to the existing leave-format.ts) exporting the map and an iconForLeaveType helper returning ReactNode, and have all four files import it: ApplyLeavePage.tsx, MyLeavePage.tsx, DashboardHome.tsx (own map at ~line 43, call site ~line 478) and AllLeaveAdminPage.tsx (own map at ~line 22, iconFor at ~line 57, call site ~line 495). Note the last two currently bake the icon into a plain string — a feed item's `meta:` field and a SelectFilter option `label` — so those two call sites need their shapes widened to accept a node. Verify as manager2@annualleave.com that the dashboard approval queue shows the neutral sick-leave icon and the annual-leave palm tree is untouched.
```

## 8. Team Leave — the Status column repeats the tab you are already on

```
In client/src/components/annual-leave/TeamLeavePage.tsx, the requests table always renders a Status column, so inside the "Pending (2)" tab every row's badge reads "Pending" — a column of identical values. Meanwhile the request's reason is not shown at all, so a manager has to open View on every row to see why the time off is being asked for. Hide the Status column when a single-status tab is active (keep it on the "All" tab, where it carries information) and use the freed width for a truncated reason preview. Keep it a column-visibility change plus one cell, not a table rewrite. Verify as manager2@annualleave.com that the Pending tab shows the reason inline for both requests and the All tab still shows Status.
```

---

Full walkthrough with screenshots: https://claude.ai/code/artifact/617631af-9156-4372-8540-f10b4b116111
