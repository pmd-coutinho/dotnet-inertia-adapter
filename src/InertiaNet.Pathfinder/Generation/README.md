# Pathfinder Generation

## Overview

This directory emits TypeScript from the normalized analysis records. It stages runtime utilities, action and named-route helpers, enum and model declarations, page props, shared props, and barrel exports before synchronizing the selected output directory.

## Interface

All C# generation classes are assembly-internal. `TypeScriptGenerator.Generate(routes, enums, pageProps, models, config, sharedProps?)` is the orchestration boundary and returns the synchronization change result.

| Collaborator | Output or responsibility |
| --- | --- |
| `ActionFileWriter.Write` | Controller action modules and the Minimal API module. |
| `RouteFileWriter.Write` | Named-route modules grouped by dot-separated route names. |
| `RouteFunctionWriter.Write` | Shared callable helper implementation for action and named-route modules. |
| `EnumFileWriter.Write` | `as const` enum objects and value-union types. |
| `ModelFileWriter.Write` | Model declarations. |
| `PropsFileWriter.Write` | Concrete page prop interfaces. |
| `PageMapFileWriter.Write` | `SharedProps`, `PagePropsMap`, `ComponentName`, and `PropsFor<T>`, including Inertia `PageProps` augmentation. |
| `RuntimeFileWriter.Write` | Root runtime module imported by generated helpers. |
| `BarrelFileWriter.WriteAll` | Exports for `actions`, `routes`, `enums`, `models`, and `types`. |
| `GeneratedOutput.Synchronize` | Staged-output synchronization and generation bookkeeping. |
| `ModulePaths`, `NameUtils`, `TypeMapper`, `TypeReferenceCollector` | Shared module-path, naming, and type-reference collaborators. |

Generated route helpers expose a callable returning `{ url, method }`, `.url`, `.definition`, and per-HTTP-method helpers. Form generation adds `.form`; discovered request contracts add payload/query/form aliases and typed `.body`, `.query`, or `.formData` markers.

Bookkeeping names declared by `GeneratedOutput` are `.pathfinder-manifest.json`, `.pathfinder-files`, `.pathfinder-inputs`, `.pathfinder.stamp`, and `.pathfinder.lock`.

## Invariants

- Generation writes into a temporary staging directory, synchronizes it, and removes the stage in `finally`. The runtime module is emitted even when there are no discovered contracts.
- Routes, enums, pages, and models are ordered deterministically with ordinal keys. Action records sharing action name, template, host, and scheme merge their HTTP methods.
- Feature switches select actions, named routes, forms, enums, models, and props. Disabling enums substitutes primitive wire types; disabling models replaces referenced page-model names with `unknown`; disabling props also removes shared props.
- Both route surfaces use `RouteFunctionWriter`. Relative runtime/model/enum imports are computed for each emitted module location. Generated identifiers reserve runtime and imported names.
- Multiple templates for one controller action are emitted as a keyed helper dictionary using host plus path. Named routes split on dots into directory groups and exported helper names.
- HTML form helpers use `get` for GET and `post` otherwise. Methods other than GET/POST add uppercase `_method` data. Typed `.body`, `.query`, and `.formData` markers are `undefined` values carrying TypeScript types.
- Enum writers quote string values, append `n` to bigint members, and protect `__proto__` property keys. Concrete page prop interfaces mark optional fields and include `[key: string]: unknown`.
- Shared props intersect page-map entries and augment `@inertiajs/core`'s `PageProps`. Models, enums, and types use named barrel exports; action and route modules use default-export aliases. Child namespaces explicitly reference their `index` modules.

## Side Effects

The generator creates and deletes temporary directories, writes TypeScript files and directories, and synchronizes output bookkeeping. It clears and repopulates `TypeMapper` enum registration for each run. The generated JavaScript runtime executes in consuming applications; analysis itself is supplied by the [Analysis package](../Analysis/README.md).

## Verification

Working directory: the repository root (`dotnet-inertia-adapter/`). The .NET test project targets .NET 10. Install the TypeScript test package's dependencies before running its npm command.

```sh
dotnet test tests/InertiaNet.Pathfinder.Tests/InertiaNet.Pathfinder.Tests.csproj
npm --prefix tests/InertiaNet.Pathfinder.TypeScript test
```

The npm script generates fixtures, typechecks, builds TypeScript, and runs the Node runtime and protocol tests.
