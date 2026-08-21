# Coding Conventions

**Analysis Date:** 2026-08-21

## Naming Patterns

### Backend (C#)

**Files:**
- Command classes: `CreateAnnualLeave.cs` — PascalCase, verb + noun
  - Nested Handler class inside: `public class Command : IRequest<Result<T>>` and `public class Handler(dependencies) : IRequestHandler<Command, Result<T>>`
  - Command/handler/validator grouped in same file folder structure: `Application/AnnualLeaves/Commands/CreateAnnualLeave.cs`
- Query classes: Same pattern as commands, e.g. `GetAnnualLeaveDetails.cs`
- Validators: Named after the request DTO: `CreateAnnualLeaveRequestValidator.cs` (in `Validators/` subfolder)
- DTOs: `CreateAnnualLeaveRequest.cs` (request payloads), `AnnualLeaveDto.cs` (response shapes)
- Domain entities: `AnnualLeave.cs`, `EmployeeProfile.cs` (singular PascalCase)
- Services: `LeaveCalculationService.cs`, `ManagerAccessScopeResolver.cs`

**Functions/Methods:**
- `public async Task<Result<T>> Handle(Command request, CancellationToken cancellationToken)` — standard MediatR handler signature
- Private helpers use PascalCase: `private async Task SeedAsync()`, `private async Task NotifyLeaveAudienceAsync(...)`
- Async methods end in `Async`: `SendEmailAsync`, `SaveChangesAsync`
- Boolean predicates start with `Is`, `Has`, or `Can`: `IsSuccess`, `HasValue`, `CanAccess`

**Variables:**
- Local variables: camelCase: `var employeeProfile`, `int? departmentId`
- Private fields: `_fieldName` (underscore prefix)
- Properties: PascalCase (auto-properties preferred)
- Constants: `UPPER_SNAKE_CASE` for compile-time constants, or PascalCase for class constants

**Types:**
- Enums: PascalCase members: `Pending`, `Approved`, `Rejected`, `Cancelled`
- Records/classes: PascalCase
- Generic constraints: Standard (TRequest, TResponse, T)

### Frontend (TypeScript/React)

**Files:**
- Components: `AnnualLeaveCard.tsx`, `AdminUsersPanel.tsx` — PascalCase
- Hooks: `useDeleteAnnualLeave.ts`, `useLeaveTypes.ts` — camelCase with `use` prefix
- API modules: `annual-leaves.ts`, `admin-users.ts` — kebab-case
- Type definitions: Exported from `lib/types/` with domain module grouping
- Test files: `ComponentName.test.tsx` or `module.test.ts`

**Functions:**
- Components: PascalCase: `function AnnualLeaveCard({ leave, user }: AnnualLeaveCardProps)`
- Helper functions in components: camelCase: `function formatDate(dateStr: string)`, `function statusColor(status: AnnualLeaveStatus)`
- Custom hooks: camelCase with `use` prefix: `export function useDeleteAnnualLeave() { ... }`
- Async API functions: camelCase, no `Async` suffix: `export async function getAnnualLeaves()`, `export async function createAnnualLeave(request)`

**Variables:**
- Destructured state: camelCase: `const [editOpen, setEditOpen] = useState(false)`
- DOM refs: camelCase: `const [actionsAnchorEl, setActionsAnchorEl] = useState<null | HTMLElement>(null)`
- Intentionally unused: underscore prefix (linting rule allows): `const _user = someArg`

**Types:**
- Interfaces/Types: PascalCase: `interface AnnualLeaveCardProps { ... }`, `type AnnualLeaveStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled'`
- String literal unions for domain enums: Single quotes, no `enum` (prefer type unions): `type AnnualLeaveStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled'`

## Code Style

### Formatting

**Backend:**
- C# conventions followed by team (implicit from `.csproj` settings)
- No explicit `.editorconfig` in repo — defaults inherited
- File-scoped namespaces: `namespace Application.AnnualLeaves.Commands;` (no closing brace)
- No pragma directives or using-statement blocks without necessity
- Blank lines separate logical groups within methods (see `CreateAnnualLeave.Handle`)

**Frontend:**
- Prettier configured via ESLint (vite.config.ts uses vitest/config)
- No explicit `.prettierrc` file; defaults used
- Import sorting: React/third-party, then local (enforced by ESLint rules)
- Emotion/MUI styled components for CSS

### Linting

**Backend:**
- No StyleCop or similar enforced; team conventions follow standard C# practices
- IntelliSense and implicit null-forgiving (`!`) operator usage indicates nullable reference types enabled

**Frontend:**
- ESLint with flat config (`client/eslint.config.js`)
- Rules:
  - `@typescript-eslint/no-unused-vars` with pattern `^_` (underscore-prefixed vars are intentionally unused)
  - `react-hooks/set-state-in-effect` and `react-hooks/purity` downgraded to `warn` (pending refactor)
  - React Hooks rules enabled; React Refresh for HMR
- Command: `npm run lint` in `client/`

### Path Aliases

**Backend:**
- None explicit; namespaces provide logical paths: `Application.AnnualLeaves.Commands`, `Domain.Interfaces`

**Frontend:**
- Bare imports with `@/` aliases configured in TypeScript (see `tsconfig.app.json`: `"moduleResolution": "bundler"`)
- API modules imported as: `import { createAnnualLeave } from '../../lib/api'` (relative paths preferred in codebase)
- No `@` alias visible in current imports; uses relative paths like `'../../lib/api'`, `'../ui'`

## Import Organization

### Backend

**Order within C# files:**
1. System and framework namespaces: `using System;`, `using Microsoft.AspNetCore.Authorization;`
2. Third-party: `using MediatR;`, `using AutoMapper;`, `using Microsoft.EntityFrameworkCore;`
3. Domain/Application: `using Domain;`, `using Application.AnnualLeaves.DTOs;`, `using Persistence;`
4. File-scoped namespace declaration last (no closing brace)

Example from `CreateAnnualLeave.cs`:
```csharp
using Domain.Interfaces;
using Application.AnnualLeaves.DTOs;
using Application.Core;
using AutoMapper;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Commands;
```

### Frontend

**Order within TypeScript files:**
1. React/React Router: `import { useState, useMemo, type MouseEvent } from 'react'`
2. Third-party (MUI, Emotion, etc.): `import Box from '@mui/material/Box'`, `import { useQuery } from '@tanstack/react-query'`
3. Local utilities/lib: `import { softBg } from '../../lib/theme-tokens'`
4. Local components: `import AnnualLeaveForm from './AnnualLeaveForm'`
5. Types: Usually with local utilities or on demand

Example from `AnnualLeaveCard.tsx`:
```typescript
import { useMemo, useState, type MouseEvent } from 'react'
import { softBg } from '../../lib/theme-tokens'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
// ... more MUI imports
import { useDeleteAnnualLeave, useLeaveTypes, useUpdateLeaveStatus } from '../../lib/hooks'
import type { AnnualLeave, AnnualLeaveStatus, UserInfo } from '../../lib/types'
import AnnualLeaveForm from './AnnualLeaveForm'
```

## Error Handling

### Backend

**Result<T> Pattern:**
- All handlers return `Result<T>`, never throw exceptions for business logic errors
- Handlers inject `Result<T>` from `Application.Core` namespace
- Success: `return Result<string>.Success(annualLeave.Id);`
- Failure variants with different HTTP semantics:
  - `Result<T>.Failure(string error)` → 404 NotFound (default)
  - `Result<T>.Conflict(string error)` → 409 Conflict (resource exists, op conflicts with state)
  - `Result<T>.Forbidden(string error)` → 403 Forbidden (user authenticated but unauthorized)
  - `Result<T>.Invalid(string error)` → 400 BadRequest (precondition on caller, not on resource)
  - `Result<T>.ValidationFailure(IDictionary<string, string[]> validationErrors)` → 400 with field errors

Example from `CreateAnnualLeave.cs`:
```csharp
var employeeProfile = await context.EmployeeProfiles
    .FirstOrDefaultAsync(ep => ep.UserId == request.AnnualLeave.EmployeeId, cancellationToken);

if (employeeProfile is null)
    return Result<string>.Failure("Employee profile not found for the selected user.");

// Later, conflict case:
var balanceError = await AnnualLeaveBalanceCalculator.CheckSufficientBalanceAsync(...);
if (balanceError is not null)
    return Result<string>.Failure(balanceError);
```

**Controller Response Handling:**
- Thin controllers dispatch to MediatR, then call `HandleResult<T>()` from `BaseApiController`
- `HandleResult<T>()` maps `Result<T>` to appropriate HTTP responses with `ApiErrorResponse` body
- See `BaseApiController.cs` for the full mapping logic

**Validation:**
- FluentValidation validators run via MediatR's `ValidationBehavior` pipeline behavior
- Add a validator class alongside the command/query (e.g., `CreateAnnualLeaveRequestValidator.cs`)
- Validators derive from `AbstractValidator<T>` and use fluent API
- Async rules call the database for uniqueness/existence checks

### Frontend

**API Error Handling:**
- Axios instance at `client/src/lib/api/client.ts` intercepts all responses
- Global error event: `emitApiError(...)` unless:
  - 401 on account/user-info endpoints (auth bootstrap, not an error)
  - 400/422 with `errors` property (field-level validation, client suppresses global notification)
  - Header `x-suppress-global-error: true` set
- Client code uses React Query for async state; mutations/queries catch errors and surface via mutation state or react-query hooks
- Forms use `react-hook-form` + `zod` for client-side validation

## Logging

### Backend

**Framework:** Serilog (`API/Extensions/LoggingExtensions.cs`)

**Configuration:**
- Console output + newline-delimited JSON to `Logs/worktrack-<date>.jsonl`
- Log retention: 14 days
- Every request carries a `CorrelationId` (also sent as `X-Correlation-ID` response header and `traceId` in error bodies)
- Correlation ID sourced from `API/Middleware/CorrelationIdMiddleware.cs`
- Log levels configurable via `Serilog` section in `appsettings.json`

**Usage:**
- `ILogger<T>` injected into handlers or services where needed
- No structured logging examples visible in handlers (most rely on exceptions for traces)
- Health checks log database and email provider state

### Frontend

**No explicit logging framework.** Console output only in dev:
- React Query DevTools available in dev
- Error events emitted to a custom `error-events.ts` module (internal event system, not external logging)
- Production errors surface via global API error handler

## Comments

### When to Comment

**Backend:**
- Inline comments explain non-obvious business logic or workarounds:
  - `// NotificationEmail encodes the display names and the free-text reason for the HTML rendering...` (line 113, CreateAnnualLeave.cs)
  - `// One transaction over both saves: the balance sync reads approved leave back...` (line 74, CreateAnnualLeave.cs)
  - `// Coverage (delegate) is optional; only validate it when one is nominated.` (line 39, CreateAnnualLeaveRequestValidator.cs)
- XML documentation (`///`) on public classes, methods, and complex properties
- No excessive inline comments; prefer clear code

**Frontend:**
- React/TypeScript code prefers clarity over comments
- MUI configuration comments explain setup choices (e.g., `vite.config.ts` service worker eviction logic)
- Test comments explain business intent or regression prevention (see test files)

### JSDoc/TSDoc

**Backend:**
- XML doc comments on public members:
```csharp
/// <summary>
/// Colleague nominated to cover urgent matters while the employee is away.
/// Optional — a request with no delegate is perfectly valid.
/// </summary>
public string? DelegateId { get; set; }
```

**Frontend:**
- Minimal; types are self-documenting via TypeScript
- Interface props documented inline:
```typescript
interface AnnualLeaveCardProps {
    leave: AnnualLeave
    user: UserInfo
}
```

## Function Design

### Size Guidelines

**Backend:**
- Handlers typically 50–150 lines (see `CreateAnnualLeave.Handle` = 112 lines)
- Logic deferred to helper functions or dedicated services when complex (e.g., `LeaveCalculationService`, `ManagerAccessScopeResolver`)
- Queries/commands kept focused on a single domain concept

**Frontend:**
- Components kept under 300 lines where possible
- Helper functions extracted for repeated logic (e.g., `formatDate`, `statusColor` at top of component file)
- Custom hooks isolate state and API logic

### Parameters

**Backend:**
- Handlers accept a single `Command` or `Query` parameter
- Dependencies injected via constructor, not method parameters
- `CancellationToken` always as final parameter
- Example: `public async Task<Result<AnnualLeaveDto>> Handle(Query request, CancellationToken cancellationToken)`

**Frontend:**
- Components accept props as single object, destructured: `function AnnualLeaveCard({ leave, user }: AnnualLeaveCardProps)`
- Hooks accept options object when multiple parameters needed
- API functions accept request objects or scalar IDs, avoid variadic parameters

### Return Values

**Backend:**
- All async work returns `Task` or `Task<T>`
- Handlers always return `Result<T>` (never throw for business logic)
- LINQ queries return `IQueryable<T>` for composition, executed with `.ToListAsync()` or `.FirstOrDefaultAsync()`

**Frontend:**
- Components return `ReactNode` (implicitly via JSX)
- Hooks return state/functions via tuple or object
- API functions return raw data (not wrapped in `Result`); React Query handles promise rejection

## Module Design

### Exports

**Backend:**
- Public command/query classes and handler live in one file (nested classes)
- Public DTOs in dedicated files
- Validators public, named after request type
- Services public for DI registration

**Frontend:**
- Components exported as default: `export default AnnualLeaveCard`
- API functions exported as named: `export async function getAnnualLeaves() { ... }`
- Hooks exported as named: `export function useDeleteAnnualLeave() { ... }`
- Types exported from `lib/types` or co-located with components

### Barrel Files

**Backend:**
- Not used; namespaces provide logical organization

**Frontend:**
- Not used; explicit imports preferred (e.g., `import { createAnnualLeave } from '../../lib/api'` vs. `import { createAnnualLeave } from '../../lib/api'`)

### File Organization

**Backend (Application Layer):**
```
Application/
├── AnnualLeaves/
│   ├── Commands/
│   │   ├── CreateAnnualLeave.cs          (Command + Handler nested)
│   │   ├── EditAnnualLeave.cs
│   │   └── DeleteAnnualLeave.cs
│   ├── Queries/
│   │   ├── GetAnnualLeaveDetails.cs      (Query + Handler nested)
│   │   └── GetAnnualLeaveList.cs
│   ├── Validators/
│   │   ├── CreateAnnualLeaveRequestValidator.cs
│   │   └── EditAnnualLeaveRequestValidator.cs
│   ├── DTOs/
│   │   ├── AnnualLeaveDto.cs             (Response shape)
│   │   ├── CreateAnnualLeaveRequest.cs   (Request payload)
│   │   └── EditAnnualLeaveRequest.cs
│   └── Support/                          (Helpers, calculators)
│       ├── AnnualLeaveBalanceCalculator.cs
│       └── AnnualLeaveMapper.cs
```

**Frontend (Components):**
```
client/src/
├── components/
│   ├── admin/
│   │   ├── AdminUsersPanel.tsx           (Component)
│   │   ├── AdminUsersPanel.test.tsx      (Tests co-located)
│   │   └── settingsPlacement.test.tsx    (Helper function tests)
│   ├── annual-leave/
│   │   ├── AnnualLeaveCard.tsx
│   │   ├── AnnualLeaveCard.test.tsx
│   │   └── AnnualLeaveForm.tsx
│   └── ui/                               (Shared/base components)
└── lib/
    ├── api/                              (API modules)
    │   ├── annual-leaves.ts
    │   ├── admin-users.ts
    │   └── client.ts                     (Axios instance)
    ├── hooks/                            (Custom hooks)
    │   └── useDeleteAnnualLeave.ts
    ├── types/                            (Shared types)
    │   └── index.ts
    └── mobx/                             (Global stores)
        ├── authStore.ts
        └── uiStore.ts
```

---

*Convention analysis: 2026-08-21*
