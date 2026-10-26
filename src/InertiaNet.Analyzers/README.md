# InertiaNet Analyzers

## Overview

This package supplies Roslyn diagnostics for Inertia component names, optional page-file validation, serializer naming settings, and Minimal API patterns that Pathfinder can discover statically. It targets `netstandard2.0` and is packaged as a compiler analyzer under `analyzers/dotnet/cs`.

## Interface

The public analyzers are sealed `DiagnosticAnalyzer` implementations. Each exposes `SupportedDiagnostics` and `Initialize`.

| Analyzer | Responsibility |
| --- | --- |
| `InertiaComponentNameAnalyzer` | Validates statically known Inertia component names. |
| `InertiaPageFileAnalyzer` | Reports missing component files when `Pages.EnsurePagesExist` is enabled in source configuration. |
| `InertiaJsonSerializerOptionsAnalyzer` | Reports non-null naming-policy assignments associated with `InertiaOptions.JsonSerializerOptions`. |
| `PathfinderMinimalApiAnalyzer` | Reports nonconstant route templates and unsupported handler expressions for recognized Minimal API registration names. |

Internal `InertiaInvocationHelpers` resolves supported Inertia invocations and constant component names; `InertiaDiagnostics` owns diagnostic descriptors. There are no code-fix providers in this directory.

## Invariants

- Analyzers opt out of generated-code analysis and enable concurrent execution.
- Page-file validation requires a statically constant `Pages.EnsurePagesExist = true` assignment. It reads literal paths and extensions from supported collection/array initializer syntax in `Pages.Paths` and `Pages.Extensions`.
- File checks apply to nonempty component names without backslashes, leading/trailing slashes, or doubled slashes. Explicit page paths resolve against the nearest project root; otherwise existing conventional page directories are searched. With no candidate roots, no missing-file diagnostic is reported.
- Default page extensions are `js`, `jsx`, `ts`, `tsx`, `vue`, and `svelte`; file lookup appends the configured extension to the slash-separated component path.
- Serializer diagnostics target non-null `PropertyNamingPolicy` and `DictionaryKeyPolicy` assignments associated with `InertiaNet.Core.InertiaOptions.JsonSerializerOptions`; options access is checked through semantic property identity.
- Minimal API registration matching recognizes member calls named `MapGet`, `MapPost`, `MapPut`, `MapPatch`, `MapDelete`, `MapMethods`, and `MapInertia`. It matches those names without checking the containing ASP.NET symbol.
- Minimal API templates must be semantic constant strings. Supported handlers are simple/parenthesized lambdas or one resolved source method/local function. `MapMethods` uses its third argument for the handler; other handler registrations use the second. `MapInertia` skips handler validation.

## Side Effects

Analyzers report diagnostics through Roslyn contexts. Page-file validation enumerates ancestor directories for project files and probes configured or conventional page directories and component files during compilation. Other analyzers inspect syntax and semantic models. They do not rewrite source files.

## Verification

Working directory: the repository root (`dotnet-inertia-adapter/`). The analyzer tests target .NET 10.

```sh
dotnet test tests/InertiaNet.Analyzers.Tests/InertiaNet.Analyzers.Tests.csproj
```
