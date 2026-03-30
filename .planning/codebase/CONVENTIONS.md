# Coding Conventions

**Analysis Date:** 2026-03-30

## C# Backend Conventions

### Naming Patterns

**Files:**
- PascalCase for all C# files: `ApplicationRepository.cs`, `DeliveryWorker.cs`, `ExceptionHandlingMiddleware.cs`
- One public class per file (strict adherence)
- Filename matches class name exactly

**Classes and Methods:**
- PascalCase for class names: `Application`, `ApplicationRepository`, `HttpDeliveryService`
- PascalCase for method names: `GetByIdAsync`, `CreateAsync`, `ProcessMessageAsync`
- PascalCase for properties: `Id`, `Name`, `ApiKeyPrefix`, `IsActive`, `CreatedAt`
- Async methods always end with `Async` suffix: `DeliverAsync`, `GetByIdAsync`, `ProcessMessageAsync`

**Variables and Parameters:**
- camelCase for local variables and parameters: `ct` (CancellationToken), `message`, `messageId`
- camelCase for private fields with underscore prefix: `_dbContext`, `_logger`, `_httpClientFactory`, `_serviceProvider`
- camelCase for parameters in method signatures: `applicationId`, `request`, `pageSize`

**Types and Enums:**
- PascalCase for enum types: `EndpointStatus`, `MessageStatus`, `AttemptStatus`, `CircuitState`
- PascalCase for enum values: `Active`, `Pending`, `Delivered`, `Failed`, `DeadLetter`, `Disabled`

**Constants and Interfaces:**
- PascalCase for interface names with `I` prefix: `IDeliveryService`, `IMessageQueue`, `ISigningService`, `IEndpointHealthTracker`
- PascalCase for static readonly constants and magic strings in usage context

### Import Organization

**Order:**
1. System namespaces: `using System;`, `using System.Collections.Generic;`
2. System.* extended: `using System.Security.Cryptography;`, `using System.Text.Json;`
3. Microsoft namespaces: `using Microsoft.AspNetCore.Mvc;`, `using Microsoft.EntityFrameworkCore;`
4. Third-party packages: `using FluentValidation;`, `using Serilog;`
5. Project-internal namespaces: `using WebhookEngine.Core.Entities;`, `using WebhookEngine.Infrastructure.Repositories;`

**File-scoped namespaces:**
```csharp
namespace WebhookEngine.Core.Entities;

public class Application
{
    // Class body
}
```

**Global usings enabled:** Projects have `<ImplicitUsings>enable</ImplicitUsings>` and global using statements in `GlobalUsings.g.cs` for `System`, `System.Collections.Generic`, `System.Linq`, `System.Threading.Tasks`.

### Code Style

**Formatting:**
- No explicit tool (Roslyn analyzers only)
- 4 spaces for indentation
- PascalCase for namespaces matching folder structure: `WebhookEngine.API.Controllers`, `WebhookEngine.Infrastructure.Repositories`
- Nullable reference types enabled: `<Nullable>enable</Nullable>`
- No trailing commas in collections

**Linting:**
- Roslyn code analyzers implicit via `<Nullable>enable</Nullable>` and null-safety checking
- No explicit ESLint or Prettier config in C# projects
- Code follows Microsoft C# coding conventions

### Comments and Documentation

**When to Comment:**
- Method and class-level summaries using `///` XML documentation
- Critical business logic explained inline with `//`
- URL references and security notes: `// NOTE:`, `// IMPORTANT:`, `// TODO:`

**JSDoc/TSDoc (XML in C#):**
```csharp
/// <summary>
/// Manages webhook applications. Each application has its own API key, signing secret, and endpoints.
/// NOTE: These endpoints are for dashboard (admin) use. API key auth is not required — dashboard cookie auth is used instead.
/// </summary>
```

**Inline comments:**
- Explain "why" not "what": "Generate API key: whe_{appIdShort}_{random32}" for clarity
- Short, direct explanations
- No block comment style

### Function/Method Design

**Size:**
- Methods typically 20-100 lines for business logic
- Background workers (DeliveryWorker) can be longer (100+ lines) due to loop structure and multiple state transitions
- Private helper methods extracted for clarity

**Parameters:**
- CancellationToken always last parameter: `async Task<T> MethodAsync(param1, param2, CancellationToken ct = default)`
- `ct` is the standard short name for CancellationToken
- Requests use `[FromBody]` or `[FromQuery]` attributes in controllers
- IOptions<T> for configuration injections

**Return Values:**
- Async methods always return `Task` or `Task<T>`, never bare async operations
- `await` is used consistently; no fire-and-forget except explicit `void connection.Start().catch(...)`
- Early returns for validation and error checks: `if (application is null) return NotFound(...)`

### Module Design

**Exports:**
- Controllers are public and inherit from `ControllerBase`
- Request/response DTOs are public inline in controller file or separate file
- Repositories are scoped services, registered in DI container
- Services implement interfaces for dependency injection

**Barrel Files:**
- Not used; explicit imports preferred

### Error Handling

**Patterns:**
- Null checks: `if (application is null) return NotFound(...)`
- Try-catch in background workers to prevent crashes: `catch (Exception ex) { _logger.LogError(...); }`
- Custom exception handling middleware: `ExceptionHandlingMiddleware` wraps all exceptions
- API responses use consistent envelope: `ApiEnvelope.Error(HttpContext, code, message)`
- Validation using FluentValidation validators: `AbstractValidator<T>` implementations

### Logging

**Framework:** Serilog with ASP.NET Core configuration
- Configuration: `builder.Host.UseSerilog((context, configuration) => configuration.ReadFrom.Configuration(context.Configuration))`
- ILogger<T> dependency injection
- Named loggers per class: `ILogger<DeliveryWorker>`, `ILogger<ExceptionHandlingMiddleware>`

**Patterns:**
- Info level for worker start/stop: `_logger.LogInformation("DeliveryWorker started. WorkerId: {WorkerId}", _workerId)`
- Error level with exception: `_logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path)`
- Warning level for expected failures: `console.warn("SignalR connection failed:", err)`
- Structured logging with named placeholders: `{WorkerId}`, `{Method}`, `{Path}`

---

## TypeScript/React Dashboard Conventions

### Naming Patterns

**Files:**
- PascalCase for React components: `PayloadViewer.tsx`, `Modal.tsx`, `EventTypeSelect.tsx`
- camelCase for utilities and hooks: `authApi.ts`, `useDeliveryFeed.ts`, `dateTime.ts`
- camelCase for types files: `types.ts`, `vite-env.d.ts`

**Functions and Components:**
- PascalCase for React components: `PayloadViewer`, `Modal`, `ConfirmModal`
- camelCase for utility functions: `login`, `logout`, `getCurrentUser`, `parseError`
- camelCase for custom hooks: `useDeliveryFeed`, `useAuthContext`
- Const arrow functions standard: `export const PayloadViewer = ({ value }: PayloadViewerProps) => { ... }`

**Variables and Parameters:**
- camelCase for all variables and parameters: `maxEvents`, `events`, `connected`, `isMounted`
- camelCase for ref names: `connectionRef`, `containerRef`
- camelCase for event handlers: `handleSuccess`, `handleFailure`, `handleClose`

**Types:**
- PascalCase for interface/type names: `DeliveryEvent`, `AuthUser`, `EndpointRow`, `MessageRow`
- `Props` suffix for component prop types: `PayloadViewerProps`, `EventTypeSelectProps`
- Leading underscore for unused parameters: `...WithMessage("at least one field must be provided.")` where message is unused

### Import Organization

**Order:**
1. React hooks and libraries: `import { useEffect, useRef, useState } from "react"`
2. Third-party packages: `import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr"`
3. Project imports (utilities, components, types, API): `import { login, logout } from "./api/authApi"`

**Example:**
```typescript
import { useEffect, useRef, useState, useCallback } from "react";
import { HubConnectionBuilder, HubConnection, LogLevel, HubConnectionState } from "@microsoft/signalr";

export interface DeliveryEvent { ... }
export function useDeliveryFeed(...) { ... }
```

### Code Style

**Formatting:**
- ESLint with TypeScript support: `eslint.config.js`
- TypeScript target: ES2020, module: ESNext
- Strict mode enabled: `"strict": true` in tsconfig.json
- Isolated modules: `"isolatedModules": true`

**Linting Rules:**
- `@typescript-eslint/no-unused-vars`: warn for unused variables (except those starting with `_`)
- `@typescript-eslint/no-explicit-any`: warn for `any` type usage
- `no-console`: warn except for `warn` and `error` methods
- `prefer-const`: error — always use const
- React hooks: `eslint-plugin-react-hooks` recommended rules

**Naming in Rules:**
```javascript
"@typescript-eslint/no-unused-vars": ["warn", {
  argsIgnorePattern: "^_",      // Parameters starting with _ are allowed unused
  varsIgnorePattern: "^_",      // Variables starting with _ are allowed unused
  caughedErrorsIgnorePattern: "^_",
}]
```

### Comments and Documentation

**When to Comment:**
- JSDoc comments for exported functions and types: `/** Connects to the SignalR /hubs/deliveries endpoint ... */`
- Inline comments only for non-obvious logic
- URL protocol explanations: `credentials: "include"` comment explaining CORS cookie behavior

**Example:**
```typescript
/**
 * Connects to the SignalR /hubs/deliveries endpoint and provides
 * a rolling list of recent delivery events (most recent first).
 */
export function useDeliveryFeed(maxEvents = 50) { ... }
```

### Function/Component Design

**Size:**
- React components typically 20-50 lines for simple presentational components
- Custom hooks (useDeliveryFeed) can be 100+ lines for complex state management and side effects
- Arrow functions preferred: `const handleSuccess = (data: DeliveryEvent) => { ... }`

**Parameters:**
- Props destructured in function signature: `({ value }: PayloadViewerProps)`
- Callback dependencies tracked in useEffect: `useEffect(() => { ... }, [push])`
- Optional parameters in interfaces: `lastLoginAt?: string | null`

**Return Values:**
- Components return JSX or null
- Custom hooks return objects with typed properties: `return { events, connected }`
- API functions return Promise-wrapped types: `Promise<AuthUser>`, `Promise<AuthUser | null>`
- Callback functions use void: `const push = useCallback((event: DeliveryEvent) => { ... }, [maxEvents])`

### Module Design

**Exports:**
- Named exports for everything: `export function useDeliveryFeed(...)`, `export interface DeliveryEvent`
- One component per file
- API module exports functions: `export async function login(...)`, `export async function logout(...)`
- Types module exports all type definitions

**Barrel Files:**
- Not explicitly used; direct imports from source files preferred

### Error Handling

**Patterns:**
- Try-catch with fallback in parseError: `try { const payload = ... } catch { return \`Request failed...\` }`
- Null checks: `if (!response.ok) throw new Error(...)`
- Expected error types handled: `if (response.status === 401) return null`
- Mounted flag pattern for cleanup in effects: `if (!isMounted) return;`

**API Error Parsing:**
```typescript
async function parseError(response: Response): Promise<string> {
  try {
    const payload = (await response.json()) as { error?: { message?: string } };
    if (payload.error?.message) return payload.error.message;
  } catch {
    // no-op
  }
  return `Request failed with status ${response.status}`;
}
```

### Logging

**Framework:** `console` methods (info, warn, error)
- Warning level for non-critical issues: `console.warn("SignalR connection failed:", err)`
- Only warn and error allowed by ESLint config
- No info/debug logging in normal flow

### Type Safety

**Patterns:**
- Strict typing everywhere: `interface AuthUser { id: string; email: string; role: string; ... }`
- Union types for status: `type MessageStatusType = "Pending" | "Sending" | "Delivered" | "Failed" | "DeadLetter"`
- Envelopes for API responses: `interface Envelope<T> { data: T }`
- Type assertion in API parsing: `const payload = (await response.json()) as Envelope<AuthUser>`
- Optional properties in types: `eventTypeIds?: string[]`, `error?: string`

---

*Convention analysis: 2026-03-30*
