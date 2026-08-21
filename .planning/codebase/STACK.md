# Technology Stack

**Analysis Date:** 2026-08-21

## Languages

**Primary:**
- C# 12 - ASP.NET Core backend (all `API/`, `Application/`, `Domain/`, `Infrastructure/`, `Persistence/` projects)

**Secondary:**
- TypeScript 5.9 - React frontend (`client/src/`)
- JavaScript - Build scripts, configuration

## Runtime

**Environment:**
- .NET 10 (SDK 10.0.100) - Backend API
- Node.js 24.x (LTS) - Frontend development and build
- Windows Server + IIS - Production hosting (see `DEPLOY.md`)

**Package Manager:**
- npm 10.x - JavaScript dependencies
- Lockfile: `client/package-lock.json` present
- NuGet - .NET dependencies (managed via `.csproj` files)

## Frameworks

**Core:**
- ASP.NET Core 10 - Web API and static file serving (minimal APIs + Controllers pattern)
- React 19.2.4 - Client UI framework
- SignalR 10.0 - Real-time WebSocket communication (`/hubs/notifications`)

**State Management:**
- MobX 6.15.0 - Client UI state (auth store, UI store)
- React Query (@tanstack) 5.95.2 - Server state and API caching

**Routing & Forms:**
- React Router 7.15.1 - Client-side routing (replaces hash-based custom router from roadmap)
- React Hook Form 7.76.1 - Form state and validation (already in use, roadmap predates this)
- Zod 4.4.3 - Runtime schema validation (already in use, roadmap predates this)

**Testing:**
- xUnit 2.9.2 - Backend unit and integration tests
- Vitest 3.2.7 - Frontend unit tests
- Microsoft.AspNetCore.Mvc.Testing 10.0.0 - Integration test host (boots real API in-process)
- Microsoft.EntityFrameworkCore.InMemory 9.0.0 - Fast test database (no constraints)
- Microsoft.EntityFrameworkCore.Sqlite 9.0.0 - Transactional test database (respects constraints)
- @testing-library/react 16.3.2 - React component testing
- jsdom 26.1.0 - DOM environment for tests

**Build/Dev:**
- Vite 6.3.5 - Frontend bundler and dev server
- TypeScript 5.9.3 - Type checking (compiled to ES2023)
- ESLint 9.39.4 - Code linting (with react-hooks plugin)
- vite-plugin-pwa 1.3.0 - Service worker and PWA manifest generation
- vite-plugin-mkcert 1.17.10 - Self-signed HTTPS certificates for local dev

**ORM & Database:**
- Entity Framework Core 9.0.0 - SQL Server ORM (no repository layer; handlers inject `AppDbContext` directly)
- Microsoft.EntityFrameworkCore.SqlServer 9.0.0 - SQL Server provider
- Persistence layer handles migrations (`Persistence/Migrations/`)

**Business Logic & API:**
- MediatR 13.0.0 - CQRS request/response pattern (Application layer handlers)
- AutoMapper 16.1.1 - DTO mapping
- FluentValidation 12.1.1 - Declarative validation (auto-runs via MediatR pipeline)
- Swagger (Swashbuckle) 6.5.0 - OpenAPI documentation and UI
- Asp.Versioning 8.1.0 - API versioning (URL and header-based)

**Infrastructure & Cross-Cutting:**
- Serilog 10.0.0 - Structured logging (console + newline-delimited JSON to `Logs/worktrack-<date>.jsonl`)
- Microsoft.Extensions.Http.Resilience 9.0.0 - Standard resilience pipeline (retry, circuit breaker, timeout)
- MailKit 4.17.0 - SMTP client for email (used by pluggable email provider)
- CloudinaryDotNet 1.28.0 - Cloud image/file upload client
- Microsoft.AspNetCore.Identity.EntityFrameworkCore 9.0.0 - User and role management

**UI Components:**
- Material-UI (@mui/material) 7.3.9 - React component library
- Material-UI Icons (@mui/icons-material) 7.3.9 - Icon set
- Emotion (@emotion/react, @emotion/styled) 11.14.x - CSS-in-JS styling
- SweetAlert2 11.26.24 - Modal/alert UI

**HTTP & API Client:**
- Axios 1.14.0 - HTTP client for browser (baseURL configured to `http://localhost:5000/api`)
- @microsoft/signalr 10.0.0 - SignalR client for WebSocket connection

## Key Dependencies

**Critical (Persistence & ORM):**
- Microsoft.EntityFrameworkCore 9.0.0 - Core ORM; no repository abstraction layer intentionally
- Microsoft.EntityFrameworkCore.SqlServer 9.0.0 - SQL Server data provider
- MediatR 13.0.0 - CQRS framework; all business logic flows through handlers

**Critical (Frontend):**
- React 19.2.4 - UI framework
- React Router 7.15.1 - Routing (migrated from custom hash-based router)
- @tanstack/react-query 5.95.2 - Server state management with caching and invalidation
- Vite 6.3.5 - Build and dev server

**Infrastructure:**
- Serilog.AspNetCore 10.0.0 - Request logging and structured logging pipeline
- FluentValidation 12.1.1 - Auto-runs validation via MediatR pipeline
- AutoMapper 16.1.1 - DTO projections in handlers
- CloudinaryDotNet 1.28.0 - Profile image and file upload
- MailKit 4.17.0 - Email delivery (SMTP relay)
- @microsoft/signalr 10.0.0 - Real-time notifications

## Configuration

**Environment:**
- Backend: `API/appsettings.json` (development, checked in); `API/appsettings.Production.json` (git-ignored, deployed separately)
- Frontend: Vite proxy configured to relay `/api/*` and `/hubs/*` to backend at `http://127.0.0.1:5000`
- Key env vars (backend):
  - `ConnectionStrings:DefaultConnection` - SQL Server connection (trusted auth locally, SQL auth remotely)
  - `Cloudinary:CloudName`, `Cloudinary:ApiKey`, `Cloudinary:ApiSecret` - Image uploads
  - `Email:Provider` - `"Brevo"` or `"Smtp"` to select email implementation
  - `Brevo:ApiKey` - Transactional email API key (Brevo HTTP API only)
  - `MailSettings:*` - SMTP config for Gmail/Brevo relay
  - `AppUrls:ApiBaseUrl`, `AppUrls:ClientBaseUrl` - CORS and link generation
  - `Slack:WebhookUrl` - Optional Slack incoming webhook

**Build:**
- `client/tsconfig.json` - References `tsconfig.app.json` and `tsconfig.node.json`
- `client/vite.config.ts` - Vite bundler, Vitest setup, PWA manifest, dev proxy
- `.csproj` files define framework target (`net10.0`), nullable types, and project references

## Platform Requirements

**Development:**
- Windows, macOS, or Linux
- .NET 10 SDK (via `global.json` and `actions/setup-dotnet@v4`)
- Node.js 24.x (from GitHub Actions workflow; local `npm` follows package.json `engines`)
- SQL Server 2022 running locally OR Docker Compose (see `docker-compose.yml`)
- HTTPS dev certificates via `vite-plugin-mkcert` (auto-generated)

**Production:**
- Deployment target: Windows Server with IIS (see `DEPLOY.md`)
- .NET 10 Hosting Bundle installed on IIS host
- SQL Server instance (configured via git-ignored `appsettings.Production.json`)
- WebSocket support required for SignalR
- React SPA published into `wwwroot/` and served by ANCM (ASP.NET Core Module v2)
- Single origin: `https://jpeople-dev.jenusplanet.com` (no CORS, no cross-site cookies)

---

*Stack analysis: 2026-08-21*
*Update after major dependency changes*
