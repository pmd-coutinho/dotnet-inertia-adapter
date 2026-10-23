# InertiaNet release tracker

Strategy lives in [ROADMAP.md](ROADMAP.md); detailed implementation evidence lives in [implementation progress](docs/implementation-progress.md). This file tracks the current upstream-alignment release.

- [x] Implement adapter protocol, serialization, resolver and validation corrections.
- [x] Implement semantic Pathfinder contracts, safe generation/build/watch behavior and endpoint validation.
- [x] Add rescue, large-integer policies, SSR operations, diagnostics and testing helpers.
- [x] Add a React/Vue CRUD playground with browser coverage for hydration/fallback/custom roots, antiforgery, validation, auth/shared props, flash, deferred loading, scroll and native forms.
- [x] Add CI gates for generated code, installed packages, starter builds and browser integration.
- [ ] Finish current installed-package/template checks and final regression runs.
- [ ] Finish the public documentation/example audit and shared version bump.
- [ ] Push the completed commits and verify CI and NuGet publishing.

Supported runtime tests execute on .NET 8, 9 and 10. TypeScript/client fixtures pin Inertia 3.0.2 and 3.9.1; the playground uses 3.9.1. BigInt marker revival is verified with 3.9.1, while string transport is verified with both clients.
