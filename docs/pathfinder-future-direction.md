# Pathfinder Future Direction

## Status

Accepted.

## Context

Pathfinder now has a stable route-generation core:

- deterministic output
- explicit unsupported-pattern diagnostics
- build/watch integration
- dedicated tests and golden files
- compatibility with Inertia Wayfinder-style objects

The remaining open design questions are no longer about whether Pathfinder should exist, but about where to push discovery and generation next without destabilizing the route contract.

## Decisions

### 1. Route Discovery Source Of Truth

Pathfinder will remain **syntax-tree-first** in the current generation.

Why:

- it keeps the tool fast and build-light
- it works before the app is runnable
- it is already good enough for the supported stable route patterns

However, broader discovery should move toward **semantic-model or endpoint-metadata** approaches when Pathfinder expands beyond the current safe subset.

That future work should target:

- broader Minimal API method-group support across files
- richer route-template resolution
- stronger binding/source inference
- fewer false negatives for supported code patterns

Short version:

- keep syntax-tree discovery for the stable route layer now
- use semantic/endpoint-backed discovery for the next major expansion

### 2. Type Generation Direction

Page prop and model generation should move toward a **semantic-model-based pipeline** before the feature set expands much further.

Why:

- syntax-only type inference is brittle for cross-file DTOs and richer generic shapes
- transitive model discovery and import generation are already stretching the syntax-only approach
- semantic analysis gives cleaner support for symbol identity, nullability, and referenced models

Short version:

- route generation can remain syntax-tree-first for now
- advanced prop/model generation should evolve toward semantic analysis

### 3. Delivery Mechanism

Generation should remain **CLI-first**, with the current MSBuild companion package as the build integration layer.

Why:

- generation writes TypeScript files, which fits a tool better than a Roslyn source generator
- the existing `pathfinder` tool plus `InertiaNet.Pathfinder.Build` already gives a workable `dotnet build` / `dotnet watch build` story
- analyzers are still valuable, but for diagnostics and editor guidance rather than file generation

Short version:

- keep TypeScript generation in the CLI/build package path
- use analyzers for warnings and guardrails, not as the primary generator

## Consequences

### Near Term

- continue hardening the current route-generation core
- expand supported Minimal API patterns only when they are safe to resolve in the syntax-tree model
- keep experimental type generation clearly marked as such

### Medium Term

- introduce a semantic-model-backed discovery/generation layer for advanced type generation
- consider endpoint metadata for richer Minimal API route coverage
- keep named endpoints as the long-term stable backend contract

### Non-Goals Right Now

- moving all generation into source generators
- replacing the CLI with analyzers
- broadening experimental generation features without stronger semantic backing
