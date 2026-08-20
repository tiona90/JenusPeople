# UX simplification prompts

Six small, independent Claude Code prompts from the 2026-08-20 employee-role UX
walkthrough (screenshots reviewed, no code changed yet). Each is scoped to one
finding and safe to run on its own — hand them to separate sessions/subagents
or work through them one at a time.

## 1. Dashboard — dedupe "Apply for leave" entry points

```
In client/src/components/annual-leave/DashboardHome.tsx, the employee dashboard offers "apply for leave" four ways in one screen: the sidebar nav item, the hero banner copy, the "No upcoming leave" empty-state card, and the Quick Actions grid. Keep the sidebar item and the "No upcoming leave" card's CTA (it's contextual — it only shows when there's nothing booked). Remove the redundant "Apply for leave" tile from the Quick Actions grid so it isn't offered a third time on the same screen. Don't touch Apply Leave's own page. Verify by running the app, logging in as an employee, and confirming the dashboard still renders correctly with one fewer Quick Actions tile.
```

## 2. Dashboard — stop repeating the weekly hours total

```
In client/src/components/annual-leave/DashboardHome.tsx, the hero banner's stat row and the "This week" card directly below it both display the identical weekly hours total (e.g. "8.0 / 40h") with near-identical progress bars. Remove the stat from the hero banner's row (keep the other hero stats like Leave remaining/Pending/Streak) and let the "This week" card be the single source of truth for that number. Verify visually in the browser that the hero banner no longer duplicates the weekly-hours figure shown in the card below it.
```

## 3. Empty-state charts on My Leave and My Timesheets

```
Two charts render a full empty 12-month grid before there's any real data to show:
- "2026 leave activity" in client/src/components/annual-leave/MyLeavePage.tsx (via AnnualLeaveList.tsx or wherever the chart component lives)
- "2026 hours by month" in client/src/components/timesheet/MyTimesheetPage.tsx

For each, add a lightweight empty state (e.g. "No activity yet this year" / "8h logged so far") that shows instead of the full chart when the underlying data has fewer than ~2 non-zero data points, and render the real chart once there's enough to be worth plotting. Keep it a simple threshold check, not a new component abstraction. Verify in the browser with the seeded employee1a@annualleave.com account (which currently has near-zero history on both).
```

## 4. Apply Timesheet — reduce daily re-entry

```
In client/src/components/timesheet/NewTimesheetPage.tsx, every day of the week has its own Project/Activity/Hours/notes fields, requiring the same Project+Activity pick to be repeated daily even when it doesn't change. Add a small "copy to rest of week" (or "same as yesterday") action on each day's row that fills in the next unfilled day(s) with the current day's Project and Activity, leaving Hours and the notes field blank for the user to fill in themselves. Keep it a plain button/icon action, not a new form abstraction. Verify by logging in as employee1a@annualleave.com, opening the current week's timesheet, filling Tuesday, and confirming the copy action populates Wednesday/Thursday/Friday's Project+Activity without overwriting locked/future-locked days.
```

## 5. Reconcile Attendance vs Timesheet numbers

```
client/src/components/attendance/AttendancePage.tsx shows "This Week: 1h 48m" (physical check-in time) while client/src/components/timesheet/MyTimesheetPage.tsx shows "8.0 / 40h logged" (allocated work hours) for the same week, with nothing explaining why they differ. Add a short, one-line explanatory caption on the Attendance page's "This Week" stat card (e.g. "Time clocked in — separate from your logged timesheet hours") so a first-time user doesn't read the mismatch as a bug. Keep it to static copy, no new logic. Verify by viewing the Attendance page in the browser and confirming the caption reads clearly next to the stat.
```

## 6. Tone down leave-type icons for sensitive categories

```
In client/src/components/annual-leave/ApplyLeavePage.tsx (and wherever the leave-type icon map is shared with MyLeavePage.tsx), Bereavement, Sick Leave, Maternity Leave, and Paternity Leave currently use the same style of small illustrated cartoon face as the lighter categories (Personal Days, Annual Leave's palm tree). Swap just those four to plainer, neutral MUI icons (e.g. HealthAndSafetyRoundedIcon or similar for Sick, FavoriteBorderRoundedIcon for Bereavement, ChildCareRoundedIcon for Maternity/Paternity) while leaving Annual Leave's palm tree and Personal Days' icon untouched. Verify visually on the Apply Leave page that all seven leave types still render with an icon, just less whimsical for the four sensitive ones.
```

---

Full walkthrough with screenshots: https://claude.ai/code/artifact/34904b4a-f651-4820-8a45-d2880e54d207
