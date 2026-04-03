# InertiaNet Todo

This file mirrors `ROADMAP.md` and is the live execution tracker.

Use `ROADMAP.md` for strategy and sequencing.
Use this file for day-to-day progress updates.

## Current Focus

- [x] Finish Milestone 1 before expanding Pathfinder again.
- [x] Decide how far to push protocol integration coverage before switching over to deeper Pathfinder hardening.
- [x] Decide whether the remaining protocol integration coverage should be sample-driven or stay test-host-driven until sample apps exist.

## Milestone 1: Core Hardening

### 1. Rendering Pipeline And Request-Scoped State

- [x] Introduce an internal request render settings object that carries resolved request-scoped values such as `Version`, `RootView`, SSR exclusion state, and any future per-request render flags.
- [x] Stop mutating `IOptions<InertiaOptions>.Value` inside `src/InertiaNet/Middleware/InertiaMiddleware.cs`.
- [x] Ensure the version used for mismatch detection in `src/InertiaNet/Middleware/InertiaMiddleware.cs` is the same version serialized into the page object in `src/InertiaNet/Core/InertiaResult.cs`.
- [x] Ensure root view overrides from `GetRootView()` flow into HTML rendering without touching shared options state.
- [x] Add integration tests covering custom `GetVersion()` and `GetRootView()` middleware overrides.

### 2. Protocol-Safe Serialization

- [x] Replace raw `JsonSerializerOptions` replacement in `src/InertiaNet/Core/InertiaOptions.cs` with a safer customization model.
- [x] Preserve protocol envelope field names and casing for `component`, `props`, `url`, `version`, `flash`, `deferredProps`, `mergeProps`, and the rest of the Inertia page contract.
- [x] Continue allowing custom converters and safe serializer tuning for prop values.
- [x] Keep script-tag-safe behavior in `src/InertiaNet/TagHelpers/InertiaTagHelper.cs` while preventing users from breaking the envelope.
- [x] Rewrite tests in `tests/InertiaNet.Tests/Core/InertiaJsonOptionsTests.cs` so they enforce protocol safety instead of allowing PascalCase envelopes.

### 3. SSR Consistency

- [x] Make `src/InertiaNet/Ssr/HttpSsrGateway.cs` use the same serializer pipeline as normal Inertia responses.
- [x] Wire `SsrOptions.ExcludePaths` from `src/InertiaNet/Core/InertiaOptions.cs` into actual SSR exclusion logic in `src/InertiaNet/Core/InertiaResult.cs`, or remove the option if per-request exclusion is the only supported model.
- [x] Stop hardcoding `wwwroot/hot` in `src/InertiaNet/Ssr/HttpSsrGateway.cs`; reuse `ViteOptions.PublicDirectory` and `ViteOptions.HotFile`.
- [x] Add integration tests for SSR success, SSR fallback, `ThrowOnError`, configured exclusions, and Vite hot-mode SSR endpoint resolution. Coverage now lives in `tests/InertiaNet.Tests/Integration/SsrFallbackIntegrationTests.cs`, `tests/InertiaNet.Tests/Integration/SsrExclusionIntegrationTests.cs`, `tests/InertiaNet.Tests/Integration/HttpSsrGatewayIntegrationTests.cs`, and `tests/InertiaNet.Tests/Ssr/HttpSsrGatewayTests.cs`.

### 4. Runtime Correctness And Test Helpers

- [x] Fix `AssertVersionRedirect()` in `src/InertiaNet/Testing/InertiaTestExtensions.cs` to match actual version mismatch behavior.
- [x] Review all testing helpers to ensure they match the protocol implemented by `InertiaMiddleware` and `InertiaResult`.
- [x] Expand the test assertion surface to make integration tests readable without drifting from real behavior.
- [x] Keep the testing helpers aligned with actual response semantics before adding more fluent APIs.

### 5. Documentation Truthfulness

- [x] Update `README.md` testing examples to use the shipped API instead of nonexistent methods.
- [x] Audit the entire root `README.md` for any claims that are not yet covered by tests.
- [x] Reduce or qualify the "complete Inertia v3 protocol compliance" language until protocol integration coverage exists.
- [x] Add a short architecture section explaining middleware flow, prop resolution, flash/error forwarding, and SSR.

### 6. Packaging Hygiene

- [x] Fix package metadata in `src/InertiaNet/InertiaNet.csproj`, including `RepositoryUrl`.
- [x] Add NuGet README metadata.
- [x] Add SourceLink and symbol package support.
- [x] Review package descriptions and tags so they reflect the compatibility-first, .NET-first positioning.

## Milestone 2: Test Matrix And Release Confidence

### 1. Multi-Target Confidence

- [x] Change `tests/InertiaNet.Tests/InertiaNet.Tests.csproj` to run across `net8.0`, `net9.0`, and `net10.0`.
- [x] Add CI coverage across all supported target frameworks.
- [x] Add CI checks for build, test, packaging, and sample validation. `.github/workflows/ci.yml` now covers main test matrix, Pathfinder tests, and package build; sample validation is intentionally omitted while the repo stays sample-free.

### 2. Protocol Integration Suite

- [x] Add end-to-end tests for initial HTML render. Implemented with `TestServer` in `tests/InertiaNet.Tests/Integration/ProtocolIntegrationTests.cs`.
- [x] Add end-to-end tests for XHR Inertia JSON responses. Implemented with `TestServer` in `tests/InertiaNet.Tests/Integration/ProtocolIntegrationTests.cs`.
- [x] Add tests for version mismatch behavior, external redirects, fragment redirects, flash forwarding, validation forwarding, clear history, preserve fragment, deferred props, once props, merge props, and prefetch behavior. Progress: version mismatch is covered in `tests/InertiaNet.Tests/Integration/RequestScopedRenderSettingsIntegrationTests.cs`; the rest are covered in `tests/InertiaNet.Tests/Integration/ProtocolIntegrationTests.cs`.
- [x] Add tests for Vary header behavior and any required response headers.

### 3. Sample-Driven Verification

- [ ] Add one MVC sample app used by tests or smoke validation. Currently not planned: protocol coverage is being driven by `TestServer` integration tests instead.
- [ ] Add one Minimal API sample app used by tests or smoke validation. Currently not planned: protocol coverage is being driven by `TestServer` integration tests instead.
- [ ] Use the sample apps to validate real SSR, Vite, validation, and route generation behavior. Currently not planned while the repo stays sample-free.

## Milestone 3: Pathfinder Hardening

### 1. Contract Corrections

- [x] Fix `.form()` generation in `src/InertiaNet.Pathfinder/Generation/ActionFileWriter.cs` and `src/InertiaNet.Pathfinder/Generation/RouteFileWriter.cs` so the generated output matches the documented contract.
- [x] Decide whether `.form()` returns hidden body data or encodes `_method` another way, then apply that consistently across runtime types, generated code, and docs.
- [x] Fix runtime parameter validation in `src/InertiaNet.Pathfinder/Generation/RuntimeFileWriter.cs` so required parameter checks actually exist and produce clear errors.
- [x] Update `src/InertiaNet.Pathfinder/README.md` to match the implemented contract exactly.

### 2. Scope Reset For Reliability

- [x] Reposition Pathfinder as a route-generation platform first.
- [x] Mark page prop generation, model generation, enum generation, and Wolverine support as experimental until they are properly tested and hardened.
- [x] Prefer named endpoints as the primary stable contract for .NET route generation.
- [x] Keep compatibility with Inertia Wayfinder-style objects as the stable frontend contract.

### 3. Route Discovery Reliability

- [x] Document the exact set of supported route discovery patterns for MVC and Minimal APIs.
- [ ] Improve `src/InertiaNet.Pathfinder/Analysis/MinimalApiRouteDiscoverer.cs` so support is not limited to only the current subset of lambda-based, literal-template patterns. Progress: static string constants/interpolations and resolvable inline `MapGroup(...)` chains are now supported; method-group handlers and truly dynamic templates remain out of scope.
- [ ] Decide whether the long-term route source of truth should remain syntax-tree based or move toward semantic analysis or endpoint metadata.
- [x] If remaining syntax-tree based for now, fail clearly on unsupported patterns instead of silently generating partial or wrong output.

### 4. Generated Code Quality

- [x] Fix missing imports and transitive model discovery for generated TS models.
- [x] Fix payload/body type generation so unknown complex types do not silently generate invalid TypeScript.
- [x] Add collision-safe file naming instead of relying only on controller short names.
- [x] Review naming conflict handling across exports, files, routes, and named route groups.
- [x] Add deterministic generation guarantees so repeated runs produce stable output.

### 5. Pathfinder Testing

- [x] Add a dedicated Pathfinder test project.
- [x] Add golden-file tests for generated actions, routes, `.form()` output, imports, naming conflicts, query parameter behavior, route constraints, and named route layout.
- [x] Add tests for supported MVC patterns.
- [x] Add tests for supported Minimal API patterns.
- [x] Add tests for unsupported patterns so failures are explicit and intentional.

### 6. Build And Watch Integration

- [x] Improve watch mode beyond the current `FileSystemWatcher` implementation in `src/InertiaNet.Pathfinder/Program.cs`.
- [x] Add an MSBuild integration story and/or a clean `dotnet watch` integration path.
- [x] Decide whether Pathfinder should remain CLI-only or also expose build-target integration for local projects.

## Milestone 4: .NET-First Platform Surface

### 1. Minimal API Ergonomics

- [x] Add first-class Minimal API helpers beyond `MapInertia()`.
- [x] Introduce a `Results.Inertia(...)` or equivalent typed result surface if the API shape feels right in ASP.NET Core.
- [x] Add better route mapping helpers for common static page scenarios.
- [x] Add docs and examples that show Minimal APIs as a first-class programming model, not a secondary port of MVC behavior.

### 2. MVC And Razor Ergonomics

- [x] Review whether current controller extensions are the right long-term API surface.
- [x] Improve Razor and TagHelper documentation.
- [x] Consider stronger view prerequisites validation and clearer error messages when Razor or TempData support is missing.

### 3. Validation And Problem Details

- [x] Strengthen the bridge between `ModelState`, `ValidationProblemDetails`, and Inertia error bags.
- [x] Add clearer guidance and helpers for Minimal API validation workflows.
- [x] Evaluate first-class support for common ASP.NET Core validation/result patterns.

### 4. Error Handling

- [x] Expand the exception handling surface around `HandleExceptionsUsing`.
- [x] Add production-ready docs and examples for custom Inertia error pages.
- [ ] Ensure custom exception rendering works consistently in MVC, Minimal APIs, and SSR scenarios. Progress: `HandleExceptionsUsing` now accepts `IResult` and `IActionResult`, recursion is guarded, and the behavior is covered in `tests/InertiaNet.Tests/Core/InertiaResultTests.cs` for the shared render pipeline.

### 5. Vite And SSR Platform Story

- [x] Tighten the Vite helper story so it feels native for ASP.NET Core apps.
- [x] Decide what the official SSR deployment story is for ASP.NET Core users.
- [x] Add end-to-end docs for development SSR, production SSR, and fallback-to-CSR behavior.

## Milestone 5: Platform Tooling

### 1. Analyzer Package

- [x] Introduce `InertiaNet.Analyzers` as a future package.
- [x] Add diagnostics for invalid page component names.
- [x] Add diagnostics for protocol-breaking serializer configuration. Implemented as an educational diagnostic because the current serializer pipeline preserves the Inertia envelope.
- [x] Add diagnostics for unsupported Pathfinder patterns.
- [ ] Add diagnostics for missing page files when page validation is enabled.

### 2. Template And Sample Package

- [ ] Introduce `dotnet new` templates after the core contracts stabilize.
- [ ] Ship at least one official React starter.
- [ ] Ship at least one official Vue starter.
- [ ] Keep sample apps in sync with docs and CI.

### 3. Optional Type Generation Expansion

- [ ] Revisit page prop and model generation only after route generation is stable.
- [ ] Consider a stronger semantic-model-based approach for type generation.
- [ ] Evaluate whether some generation should move toward source generators or analyzers rather than remaining in a CLI-only tool.

## Immediate Next Batch

- [x] Fix request-scoped `Version` and `RootView` flow.
- [x] Replace unsafe JSON envelope customization with a protocol-safe approach.
- [x] Align SSR serializer behavior and SSR config usage.
- [x] Fix `AssertVersionRedirect()` and README testing examples.
- [x] Add end-to-end integration tests that prove the corrected behavior.

## Notes

- The current test project targets `net10.0` so the suite can run in the local environment. Restoring multi-target coverage remains part of Milestone 2.
- `ROADMAP.md` remains the strategic source of truth. This file should be updated whenever roadmap work lands.
