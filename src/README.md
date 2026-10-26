# Source Packages

## Overview

The source tree separates the request-time ASP.NET Core adapter from
development-time contract generation, compiler diagnostics, build integration,
and starter-template distribution.

## Interface

| Package | Boundary |
| --- | --- |
| [InertiaNet](./InertiaNet/README.md) | `IInertiaService`, page results, prop capabilities and resolution, middleware, SSR, Razor/Vite helpers, diagnostics, and HTTP assertion utilities. |
| [InertiaNet.Pathfinder](./InertiaNet.Pathfinder/README.md) | The `pathfinder` CLI evaluates project inputs and emits TypeScript route and serialization contracts. |
| [InertiaNet.Analyzers](./InertiaNet.Analyzers/README.md) | Roslyn diagnostics for component names, page files, serializer settings, and statically discoverable Minimal API registrations. |
| [InertiaNet.Templates](./InertiaNet.Templates/README.md) | Content-only NuGet distribution of React and Vue `dotnet new` starters. |
| [InertiaNet.Pathfinder.Build](./InertiaNet.Pathfinder.Build/InertiaNet.Pathfinder.Build.csproj) | Opt-in MSBuild companion supplied through its [properties](./InertiaNet.Pathfinder.Build/buildTransitive/InertiaNet.Pathfinder.Build.props) and [targets](./InertiaNet.Pathfinder.Build/buildTransitive/InertiaNet.Pathfinder.Build.targets). |

The build companion has no C# source contract; its usage and properties are
documented in [Pathfinder build integration](./InertiaNet.Pathfinder/README.md#build-integration).

## Invariants

- Package versions derive from `InertiaNetPlatformPackageVersion` in the
  repository's shared MSBuild properties.
- The runtime and CLI target .NET 8, 9, and 10. Analyzers target .NET Standard
  2.0; the template package carries .NET 10 applications as content.
- Runtime prop and protocol behavior belongs to `InertiaNet`. Pathfinder
  generates contracts from source/configuration without starting the app;
  compiler analyzers report source diagnostics.
- Build integration defaults to disabled. Project evaluation disables
  Pathfinder to prevent recursive generation; enabled generation uses the
  configured tool and shared-output framework selection.
- Template projects retain a package-version placeholder in source. Stage and
  substitute that value before packaging and instantiating the starters.

## Side Effects

- The runtime can write responses and TempData, invoke application callbacks,
  read manifests, send SSR requests, and emit telemetry.
- Pathfinder reads project/source inputs, invokes MSBuild evaluation, and writes
  TypeScript output. Enabled build integration invokes the CLI and writes
  incremental input fingerprints under the intermediate output directory.
- Analyzers report diagnostics and may probe page files. Template staging and
  packaging copy content, replace version tokens, and create package artifacts.

## Verification

From the repository root:

```bash
dotnet test tests/InertiaNet.Tests/InertiaNet.Tests.csproj
dotnet test tests/InertiaNet.Pathfinder.Tests/InertiaNet.Pathfinder.Tests.csproj
dotnet test tests/InertiaNet.Analyzers.Tests/InertiaNet.Analyzers.Tests.csproj
```

Use the [generator contract](./InertiaNet.Pathfinder/README.md#verification) for
generated TypeScript checks, the [template contract](./InertiaNet.Templates/README.md#verification)
for staged smoke builds, and [script verification](../scripts/README.md#verification)
for installed-tool/build-companion integration. SDK/runtime requirements are
documented in each package contract.
