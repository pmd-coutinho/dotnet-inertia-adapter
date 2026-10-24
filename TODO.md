# InertiaNet release tracker

Strategy lives in [ROADMAP.md](ROADMAP.md); detailed implementation evidence lives in [implementation progress](docs/implementation-progress.md). This file tracks the current upstream-alignment release.

- [x] Implement adapter protocol, serialization, resolver and validation corrections.
- [x] Implement semantic Pathfinder contracts, safe generation/build/watch behavior and endpoint validation.
- [x] Add rescue, large-integer policies, SSR operations, diagnostics and testing helpers.
- [x] Add a React/Vue CRUD playground with browser coverage for hydration/fallback/custom roots, antiforgery, validation, auth/shared props, flash, deferred loading, scroll and native forms.
- [x] Add CI gates for generated code, installed packages, starter builds and browser integration.
- [x] Complete installed-package/template checks and final regression runs.
- [x] Complete the public documentation/example audit and align the version prefix to Laravel v3.5.2.

Publication follows the completed push through [CI](https://github.com/pmd-coutinho/dotnet-inertia-adapter/actions/workflows/ci.yml) and [automatic NuGet publishing](https://github.com/pmd-coutinho/dotnet-inertia-adapter/actions/workflows/publish-prerelease.yml). Those live results are authoritative for each release; local package checks use `3.5.2-alpha.1`, while CI assigns `3.5.2-alpha.<CI run>`.

Supported runtime tests execute on .NET 8, 9 and 10. TypeScript/client fixtures pin Inertia 3.0.2 and 3.9.1; the playground uses 3.9.1. BigInt marker revival is verified with 3.9.1, while string transport is verified with both clients.
