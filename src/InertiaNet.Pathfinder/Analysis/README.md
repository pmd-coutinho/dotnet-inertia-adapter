# Pathfinder Analysis

## Overview

This directory converts C# syntax, Roslyn symbols, explicit serialization profiles, and optional registered-endpoint snapshots into the contracts consumed by TypeScript generation. The CLI composes MVC, Minimal API, and optional Wolverine route discovery, then uses `SemanticContractDiscoverer` to produce routes, enums, models, page props, and shared props.

## Interface

All analysis types are assembly-internal. Their public members are implementation boundaries for Pathfinder and its tests.

| Boundary | Contract |
| --- | --- |
| `ControllerRouteDiscoverer.Discover(tree)` | Discovers attribute-routed controller actions as `List<RouteInfo>`. |
| `MinimalApiRouteDiscoverer.Discover(tree, semantic?)` | Discovers statically resolvable Minimal API registrations, including `MapInertia`. |
| `WolverineRouteDiscoverer.Discover(tree)` | Optional route discovery selected by the CLI's Wolverine switch. |
| `SemanticContractDiscoverer(...).Discover(trees, routes)` | Returns `Routes`, `Enums`, `Models`, `Pages`, and `SharedProps` for generation. |
| `ProjectCompilationLoader.LoadAsync(...)` | Builds an analysis compilation and returns root syntax trees plus tracked input paths in `ProjectCompilation`. `CreateForSources(...)` supports source-only compilation. |
| `ContractConfiguration.Load(path)` | Loads separate output and input `SerializationProfile` settings. |
| `EndpointManifestValidator.Validate(routes, path, config)` | Compares source routes with an exported registered-endpoint snapshot. |
| `RouteTemplateParser.Parse/StripConstraints` | Converts ASP.NET route patterns into typed parameter parts and normalized output templates. |
| `RouteParameterDiscovery.Discover(...)` | Matches route placeholders to C# parameter syntax, including `FromRoute` aliases. |
| `ContractNames.Assign(...)` | Assigns stable generated identifiers from type identities and preferred names. |
| `PathfinderDiagnostics` | Accumulates `PathfinderDiagnostic` entries and writes sorted text or JSON diagnostics. |

The contract records are `RouteInfo`/`RouteParameter`, `EnumInfo`/`EnumMember`, `ModelInfo`/`ModelProperty`, and `PagePropsInfo`/`PropField`. They preserve HTTP methods, source locations, host/scheme information, parameter defaults and catch-alls, wire types, optional/deferred fields, and available validation metadata.

`EnumDiscoverer`, `PropsDiscoverer`, and `ModelDiscoverer` also expose syntax-based discovery helpers. The CLI's current type-contract pipeline uses `SemanticContractDiscoverer`.

## Invariants

- Analysis builds compilations without loading or executing application assemblies. Circular project references fail; missing referenced source inputs and targeting packs produce diagnostics.
- Unresolved Minimal API templates or inline group prefixes are skipped with diagnostics. Supported registration names include `MapGet`, `MapPost`, `MapPut`, `MapPatch`, `MapDelete`, `MapMethods`, and `MapInertia`.
- Route parsing uses ASP.NET `RoutePatternFactory` semantics. Defaults, optional parameters, catch-alls, and constraint-derived primitive types survive normalization. `CatchAll` values `1` and `2` distinguish encoded from preserved slashes.
- Placeholder matching is case-insensitive. Body, query, form, header, and service parameters are excluded from route-parameter candidates; `FromRoute(Name = ...)` supplies aliases.
- Output profiles default to camelCase and `WhenWritingNull`. Input profiles default to no omission and `AllowReadingFromString`. Explicit configuration rejects unknown JSON members and invalid enum settings.
- Configured type overrides accept only finite unions of `string`, `number`, `boolean`, `bigint`, `null`, and `unknown`. They cannot inject executable TypeScript or imports.
- Contract names are assigned in ordinal identity order, protect reserved and companion `Validation` names, handle case-insensitive collisions, and use stable hash suffixes where required.
- Endpoint snapshots require `schemaVersion: 1` and well-formed endpoint entries. Validation compares route names or paths, HTTP method sets, hosts, defaults, and available component metadata. Named registered endpoints require discovered counterparts; mismatches report `PATHFINDER400`–`PATHFINDER402`.
- Diagnostic storage is scoped with `AsyncLocal`. Text and JSON output are sorted by file, line, code, and message; JSON property names use camelCase.

## Side Effects

Analysis reads source, metadata, explicit contract JSON, and endpoint snapshots. Referenced projects are evaluated through the parent package's source loader. Discovery updates diagnostic state; `PathfinderDiagnostics.Write` writes to its supplied `TextWriter`. TypeScript file generation belongs to the [Generation package](../Generation/README.md).

## Verification

Working directory: the repository root (`dotnet-inertia-adapter/`). The test project targets .NET 10.

```sh
dotnet test tests/InertiaNet.Pathfinder.Tests/InertiaNet.Pathfinder.Tests.csproj
```
