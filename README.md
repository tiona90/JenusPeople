# Jenus People

A full-stack **leave management and timesheet tracking** application — employees
submit annual leave and log timesheet hours; managers and admins review,
approve, and track across departments.

Built with **ASP.NET Core 10** (Clean Architecture) and **React 19**
(TypeScript + Vite).

---

## Features

- **Annual leave** — submit, approve, and track requests with automatic
  working-day calculation (configurable working days, public holidays).
- **Timesheets** — log daily hours against projects; Draft → Submitted →
  Approved/Rejected workflow with status history.
- **Department scoping** — role-based visibility; managers see only their
  departments.
- **Real-time notifications** — SignalR pushes updates; React Query refreshes
  affected views automatically.
- **Email** — confirmations and reminders (check-in/out, pending approvals,
  low balance, birthdays) via a pluggable Brevo / SMTP provider.
- **Authentication** — cookie-based Identity, plus optional Google and GitHub
  OAuth.

## Tech stack

| Layer | Technology |
|-------|-----------|
| Backend | ASP.NET Core 10, MediatR (CQRS), EF Core, FluentValidation |
| Database | SQL Server |
| Frontend | React 19, TypeScript, Vite, MobX, React Query, MUI |
| Real-time | SignalR |
| Media | Cloudinary (profile images, evidence uploads) |

## Architecture

Clean Architecture with strict layer boundaries:

```
Domain          → no dependencies
Persistence     → depends on Domain
Application     → depends on Domain (MediatR CQRS)
Infrastructure  → depends on Application (Email, Config)
API             → depends on all layers
client/         → React SPA
```

Business logic lives in `Application/*/Commands` and `Application/*/Queries`;
handlers return `Result<T>` and controllers stay thin.

## Getting started (development)

**Prerequisites:** .NET 10 SDK, Node.js, SQL Server.

```bash
# Backend (from the solution root) — http://localhost:5000
dotnet run --project API

# Frontend (from client/) — http://localhost:5173
npm install
npm run dev
```

Backend configuration lives in `API/appsettings.Development.json` (connection
string, email, OAuth, Cloudinary). The database schema is created and seeded
automatically on first run via EF Core migrations.

### Database migrations

```bash
dotnet ef migrations add <Name> --project Persistence --startup-project API
dotnet ef database update --project Persistence --startup-project API
```

## Production build & deployment

The app is designed for **same-origin hosting**: the ASP.NET Core app serves
both the API (`/api`, `/hubs`) and the built React SPA from `wwwroot/`.

```powershell
# Produces a deployable release in publish\jpeople
powershell -ExecutionPolicy Bypass -File .\build-release.ps1
```

See **[DEPLOY.md](DEPLOY.md)** for the full IIS setup guide.

## Project structure

```
Domain/          Entities and core types
Persistence/     EF Core DbContext, migrations
Application/     CQRS handlers, validators, DTOs
Infrastructure/  Email providers, external integrations
API/             Controllers, middleware, startup, wwwroot (SPA)
client/          React SPA
WorkTrack.Tests/ Unit tests
```
