# Deploying Jenus People to IIS (same-origin, single site)

The React SPA and the ASP.NET Core API are served from **one** IIS site at
`https://jpeople.jenusplanet.com`. The API serves the SPA from `wwwroot/` and
its own endpoints under `/api` and `/hubs`. This means **no CORS and no
cross-site cookies** — everything is one origin.

Target server: **185.190.143.89** (Windows Server + IIS, hostname `VMI716398`).

---

## 1. Build the release (on your dev machine)

From the solution root:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-release.ps1
```

This produces `publish\jpeople\` containing the API, `web.config`, the
`appsettings*.json` files, and the built SPA in `wwwroot\`. Copy that whole
folder to the server (e.g. `C:\sites\jpeople`).

> `appsettings.Production.json` holds the production DB password and is
> **git-ignored** — it ships only with the release folder, never via git.

---

## 2. One-time server prerequisites

1. **.NET 10 Hosting Bundle** — installs the .NET runtime + the ASP.NET Core
   Module v2 (ANCM) that `web.config` relies on.
   Download → "ASP.NET Core Hosting Bundle" from the .NET 10 downloads page,
   install, then run `iisreset`.
2. **WebSocket Protocol** Windows feature — required for SignalR
   (`/hubs/notifications`). Server Manager → Add Roles and Features → Web
   Server (IIS) → Application Development → **WebSocket Protocol**.
3. **TLS certificate** for `jpeople.jenusplanet.com` (the app sets `Secure`
   auth cookies, so HTTPS is required to log in). Use an existing cert or
   `win-acme` for Let's Encrypt.
4. **DNS**: `jpeople.jenusplanet.com` → `185.190.143.89`.

---

## 3. Database (SQL Server)

The app **auto-creates its schema on startup** (`context.Database.MigrateAsync()`).
Seeding is separate and opt-in — see §3.1. You only need the login and an
(empty) database it can own.

> `ppluser` has no `CREATE DATABASE` permission, so the database **must already
> exist** before the first start. If it doesn't, startup logs
> `CREATE DATABASE permission denied in database 'master'` and — because the
> migrate/seed block only logs its exception — **the site still comes up, just
> with no tables**. Always check the log after a first deploy rather than
> trusting that the site loaded.

Run once on the production SQL Server (adjust `Server=` if SQL is remote):

```sql
-- Login used by the app (matches appsettings.Production.json)
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'ppluser')
    CREATE LOGIN [ppluser] WITH PASSWORD = 'P30pl3123#', CHECK_POLICY = OFF;
GO
-- Empty database
IF DB_ID('jpeople_dev') IS NULL CREATE DATABASE [jpeople_dev];
GO
USE [jpeople_dev];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'ppluser')
    CREATE USER [ppluser] FOR LOGIN [ppluser];
ALTER ROLE [db_owner] ADD MEMBER [ppluser];
GO
```

> **Check the connection string** in `appsettings.Production.json`:
> `Server=185.190.143.89`. If SQL Server runs on the **same box** as IIS,
> change it to `Server=localhost` (or `Server=.`) for a faster loopback.
> Also ensure SQL Server has **Mixed-Mode authentication** enabled (SQL logins)
> and **TCP/IP** turned on if connecting by IP.

### 3.1 Seeding the demo data

Seeding is controlled by three flags in `appsettings.Production.json` (the
git-ignored file that ships in the release folder):

```jsonc
"Seed": {
  "Enabled": false,             // run DbInitializer at all
  "DemoData": false,            // also create the demo managers/employees + their data
  "AllowInProduction": false    // permit account creation / password resets on a Production host
}
```

All three default to **`false`**, which is the safe production posture: nothing
is seeded and no password is ever touched.

> ⚠️ **These flags live in a git-ignored file, so a release does not update
> them.** Check the `appsettings.Production.json` already on the server — if it
> still carries `"Enabled": true, "DemoData": true` from an earlier deploy, it
> keeps that setting until you edit it there.

`AllowInProduction` is the guard rail. When `ASPNETCORE_ENVIRONMENT=Production`
and it is not set, `DbInitializer` refuses the two things that plant the
hardcoded password — it will not create a seed account, and it will not reset an
existing one's password — and it skips the demo accounts regardless of
`DemoData`. The cleanup that *deletes* stale demo accounts still runs. Startup
logs a warning whenever it cuts a run back this way.

`DbInitializer` splits what it writes in two, so the flags decide between them:

**Reference, structural and maintenance data — `Enabled: true` is enough**

| Data | Rows |
|---|---|
| Roles | 3 |
| Departments | 5 |
| Leave types / activity types | 8 / 8 |
| App settings | 1 |
| Admin user, profile, department assignment | 1 / 1 / 1 |
| Backfills: leave-type design fields, project metadata, zero entitlements | in place |

A real tenant wants all of this. The three backfills repair rows that predate a
migration and run on every start.

**Illustrative content — also needs `DemoData: true`**

| Data | Rows |
|---|---|
| Demo users (2 managers + 8 employees) | 10 |
| Their profiles / department assignments | 10 / 2 |
| Projects | 3 |
| Annual leave requests | 2 |
| Timesheets / timesheet entries | 1 / 2 |

These are worked examples. On a live database the sample leave request and
timesheet are indistinguishable from a member of staff having booked leave and
logged a week of hours they never logged, which is why they are gated rather than
left to fall away with the demo accounts.

On a Production host you need **all three** flags `true` to get the second table.

Log in as `admin@annualleave.com`; every seeded account uses the password
`Pa$$w0rd`.

**Read these before leaving the flags on:**

- ⚠️ **All 11 accounts share the hardcoded password `Pa$$w0rd`**, and
  `EnsurePassword` **resets it on every startup**. So with all three flags on,
  changing Admin's password in the UI is undone by the next app-pool recycle.
  This is fine for a demo/UAT site on `jpeople_dev`; it is **not** acceptable for
  a real tenant with real staff data. `AllowInProduction: false` is what stops
  it — leave it off and the reset cannot happen no matter what the other two
  flags say.
- ⚠️ **Flipping `DemoData` back to `false` deletes the demo data.** On the next
  start `SeedUsers` removes all ten demo accounts *and their dependent rows*
  (profiles, leave, timesheets) so only Admin remains. Leave it `true` for as
  long as you want the demo accounts to exist — don't treat it as a one-shot
  fill. The three sample projects are *not* deleted — they only lose their owner,
  since a project is real work and losing it with the account that happened to own
  it would be worse. Remove them by hand if you don't want them.
- ⚠️ **`Enabled: false` does not clean up demo accounts already in the database.**
  It stops the seeder running at all, which also stops the teardown. See §3.2.
- To go live for real: set **all three** flags to `false`, restart, then set a
  strong Admin password. With `Enabled: false` the seeder never runs; even if
  something turns it back on, `AllowInProduction: false` keeps the password from
  being reset.
- Each individual seeder is a no-op when its table already has rows, so an
  existing database is never overwritten — it only gains what's missing.

### 3.2 Removing demo accounts from a database that already has them

Any database seeded with `DemoData: true` carries ten accounts whose password is
the published `Pa$$w0rd`. **Setting `Enabled: false` does not remove them** — it
stops the seeder entirely, and the seeder is what performs the teardown. Run this
once on each such database:

1. In `appsettings.Production.json`, set `Enabled: true`, `DemoData: false`,
   `AllowInProduction: false`.
2. Recycle the app pool. `SeedUsers` deletes the ten demo accounts and their
   profiles, leave, timesheets and department assignments. `AllowInProduction:
   false` keeps Admin's password untouched throughout.
3. Confirm: `SELECT Email FROM AspNetUsers ORDER BY Email` — only
   `admin@annualleave.com` and accounts you created should remain.
4. Set `Enabled: false` and recycle again.

> Before this was fixed, step 2 silently did nothing. `DbInitializer`'s cleanup had
> drifted behind `DeleteAdminUser` and no longer detached `Project.OwnerId`, which
> the sample projects set to manager1/manager2. Deleting those accounts hit a
> `Restrict` foreign key, threw, and was swallowed by the migrate/seed `catch` — so
> the demo accounts survived every restart meant to remove them, and every seeder
> after `SeedUsers` was skipped for that startup. If you tried this before and the
> accounts came back, that is why.

If the demo accounts are not present, skip all of this and leave `Enabled: false`.

---

## 4. IIS configuration

### Application Pool
- Add Application Pool → name `jpeople`.
- **.NET CLR version = "No Managed Code"** (ANCM hosts the runtime itself).
- Pipeline: Integrated. (Optionally set Start Mode = AlwaysRunning.)

### Site
- Add Website → name `jpeople`, physical path = `C:\sites\jpeople`
  (the folder you copied), Application Pool = `jpeople`.
- Binding: **https**, port 443, host name `jpeople.jenusplanet.com`, select the
  TLS certificate. (Add a port-80 http binding too if you want an http→https
  redirect.)

### Permissions
- Grant the app-pool identity (`IIS AppPool\jpeople`) **Read** on the site
  folder and **Modify** on `Logs\` and `logs\`. `Logs\` is where the app writes
  its own structured log (§7) and is **required**, not optional — without it
  Serilog's file sink cannot open a file. `logs\` is the separate ANCM stdout
  directory used only when you enable stdout logging for troubleshooting (§6).

### Reverse proxy (only if you add one)
ANCM in-process needs no configuration here: IIS hands the app the real client
address, which is what the rate limiters partition on. If you ever put another
tier in front (nginx, ARR, Cloudflare), list its addresses in
`appsettings.Production.json` so the app trusts their `X-Forwarded-For`:

```json
"ForwardedHeaders": { "KnownProxies": [ "10.0.0.9" ] }
```

Leave it empty otherwise. The header is only honoured from the addresses listed,
and while the list is empty it is ignored entirely — a caller that could set its
own forwarded address would get a fresh rate-limit partition per request and
neither the 100/min global cap nor the 5/min login cap would hold. A value that
is not an IP address stops startup rather than being skipped.

The environment is already pinned to **Production** in `web.config`
(`ASPNETCORE_ENVIRONMENT=Production`), so `appsettings.Production.json` loads
automatically.

---

## 5. Email (Brevo) — important gotcha

The app sends mail via the Brevo HTTP API, and the Brevo account has
**"Authorised IPs"** enabled. **Add the server's public IP
(`185.190.143.89`) to the Brevo allowlist**, or every send fails with
`401 unrecognised IP`. See https://app.brevo.com/security/authorised_ips.
(The existing IPv4-pinning code only fixes *which* local IP is used; the
server IP still has to be allowlisted.)

---

## 6. Verify

1. Browse to `https://jpeople.jenusplanet.com` → the login page loads
   ("Jenus People" branding).
2. Log in with a seeded account → dashboard renders, no console CSP errors.
3. Real-time: trigger a notification → it arrives without a refresh (confirms
   the SignalR WebSocket is working).

If the site returns **HTTP 500.30/500.31/502.5** (ANCM startup failure):
- Temporarily set `stdoutLogEnabled="true"` in `web.config`, create the
  `logs\` folder, recycle the pool, reproduce, then read `logs\stdout_*.log`.
- Usual causes: Hosting Bundle missing/older than the app, DB unreachable, or a
  bad connection string. A database the app cannot migrate is fatal by design
  outside Development, and the reason is the first thing in `stdout_*.log`.

---

## 7. Logging and health probes

### Logs
Serilog writes two places:

- **Console** — a readable line per event. IIS discards it unless you enable ANCM
  stdout logging (§6).
- **`Logs\worktrack-<date>.jsonl`** in the site folder — one JSON object per line,
  a file per day, the last 14 kept. This is the one to query.

Every log line from a request carries a `CorrelationId`, which is also returned to
the caller in the `X-Correlation-ID` response header and appears as `traceId` in
error response bodies. So a user reporting an error can be answered from the log
without guessing at timestamps:

```powershell
Get-Content Logs\worktrack-*.jsonl | Select-String '"CorrelationId":"<the id>"'
```

A caller may send its own `X-Correlation-ID` to tie a chain of calls together; the
app accepts it only if it is url-safe and under 64 characters, and generates one
otherwise.

To change levels on a deployed host, add a `Serilog` section to
`appsettings.Production.json` and recycle the app pool — it overrides the defaults
compiled in (`Information`, with `Microsoft.AspNetCore` at `Warning`). For example,
to see EF Core SQL:

```json
"Serilog": {
  "MinimumLevel": {
    "Override": { "Microsoft.EntityFrameworkCore.Database.Command": "Information" }
  }
}
```

### Probes

| Endpoint | Checks | Answers |
|---|---|---|
| `GET /health` | none | 200 while the process serves requests |
| `GET /health/ready` | database, mail provider | 200 healthy or degraded, 503 unhealthy |

Both are anonymous and exempt from rate limiting, and both return status names
only — no exception text, no connection details.

Point a monitor at **`/health/ready`** and a restart-style probe at **`/health`**.
The split matters: `/health` deliberately ignores dependencies, because restarting
this app does not repair a database it cannot reach. A **database** failure is
`Unhealthy` (503, stop sending traffic here). A **mail provider** failure is only
`Degraded` — notifications are late, but booking leave and filling timesheets still
work, so the instance stays in rotation and the named check is what should raise an
alert. The mail probe result is cached for 5 minutes, so a provider that has just
recovered can read as degraded for that long.

---

## Redeploying later

Re-run `build-release.ps1`, stop the `jpeople` app pool (to release file
locks), copy the new `publish\jpeople\*` over the site folder, start the pool.
`appsettings.Production.json` already on the server can be left in place
(don't overwrite it if you keep secrets only on the server).
