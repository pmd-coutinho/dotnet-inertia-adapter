# Pathfinder architecture decisions

Status: accepted; the semantic/project foundation is implemented.

## Discovery

Keep fast source discovery for documented route declarations. Use Roslyn semantic models for constants, source method groups, CLR type identity and serialized contracts. Evaluate the actual MSBuild project, selected framework/configuration, compile items and restored project/package references without building or loading the application.

Registered endpoint metadata is an explicit second source of evidence. Applications can export a versioned snapshot and validate source discovery in CI, including conventions and conditional registration. The generator does not start the application.

## Contract generation

Use a shared internal representation for routes, parameter bindings, JSON/request types, page/shared props and validation hints. Named-route and controller-action writers consume the same route emitter. Keep output JSON, JSON input and query/form binding contracts separate. Explicit profiles describe serializer customization; unsupported dynamic shapes produce diagnostics or `unknown`.

Type identity includes namespaces, assemblies, closed generic arguments and their nullability. Generated names/paths account for reserved names, case collisions, runtime imports and companion exports. Compile generated output against real Inertia declarations, then verify HTTP/serialization/browser behavior independently.

## Delivery

Retain the CLI and optional MSBuild companion. Stage output, retain last-good files on failures, own only generated paths, and preserve unchanged module timestamps. Strict/check modes, structured diagnostics and tracked external inputs apply to ordinary generation, watch mode and builds.

## Scope

Documented route helpers remain the stable surface. Serialization-aware type output and Wolverine discovery remain experimental while their supported patterns expand. Custom binders, grouped parameters, dynamic validators and polymorphic/converter schemas need explicit policies and fixtures before broader support is advertised.

See [ROADMAP.md](../ROADMAP.md), [implementation evidence](implementation-progress.md), and the [upstream research](inertia-pathfinder-research-2026-10-23.md).
