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

The app **auto-creates its schema and seed data on startup**
(`context.Database.MigrateAsync()` + `DbInitializer.SeedData`). You only need
the login and an (empty) database it can own.

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
  folder and **Modify** on a `logs\` subfolder (used if you enable stdout
  logging for troubleshooting — see §6).

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
  bad connection string.

---

## Redeploying later

Re-run `build-release.ps1`, stop the `jpeople` app pool (to release file
locks), copy the new `publish\jpeople\*` over the site folder, start the pool.
`appsettings.Production.json` already on the server can be left in place
(don't overwrite it if you keep secrets only on the server).
