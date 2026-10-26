# Prop Resolution

## Overview

Internal request-local traversal resolves selected values and collects the metadata used to construct an Inertia page.

## Interface

- Internal `PropsResolver.ResolveAsync` combines shared and page props and returns resolved props plus protocol metadata.
- Internal `PropValueResolver` unwraps delegates, tasks, `Lazy<T>`, and `ValueTask<T>`.
- Internal `OnceMetadata` and `ScrollMetadataValue` fix protocol metadata names and preserve required nullable fields.

## Invariants

- Partial filtering requires `X-Inertia` and an exact match between the partial component and the current component.
- Providers expand before traversal; page values replace shared entries. Ordinary dot keys unpack into nested objects; keys marked literal retain their spelling.
- Exclusion happens before resolving a value. `AlwaysProp` bypasses partial exclusion; optional/deferred props are excluded from initial output. Once keys reported as loaded can suppress non-partial Inertia resolution unless refreshed.
- Factories returning wrappers inherit the returned wrapper's exclusion rules and metadata. Lazy and wrapper unwrapping each have a 32-step bound.
- Prop delegates must return a value and accept either no arguments or `IServiceProvider` plus `CancellationToken`.
- Traversal uses configured JSON contracts and dictionary naming policies. Custom converters own values with `JsonTypeInfoKind.None`; cycles or excessive depth throw `JsonException`.
- Cancellation propagates. Eligible non-cancellation failures on a rescuable prop are logged, omitted from resolved values, and recorded in rescued metadata.
- Scroll metadata records pagination identifiers and reset state using fixed protocol field names.

## Side Effects

Traversal executes selected callbacks, providers, lazy values, property getters, and enumerators. It builds request-local metadata, configures scroll merge intent, records optional diagnostics, and logs rescued failures.

## Verification

Run from the repository root:

```sh
dotnet test tests/InertiaNet.Tests/InertiaNet.Tests.csproj
```

The test project targets `net8.0`, `net9.0`, and `net10.0`.
