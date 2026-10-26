# Runtime Diagnostics

## Overview

Optional diagnostics measure prop resolution and rendering; explicit manifest capture exports registered endpoint metadata.

## Interface

- `InertiaEndpointManifest.Capture` accepts an endpoint route builder or endpoint data sources; `WriteAsync` exports the snapshot.
- `InertiaEndpointDescription` records route names, patterns, methods, hosts, component/controller/action, order, and matching/link-generation suppression.
- `InertiaComponentMetadata` attaches a declared component to an endpoint.
- `InertiaPropDiagnostic` records path, kind, outcome, duration, slow status, route name, deferred group, and once key.
- `InertiaRenderDiagnostic` records component, route, format, bytes, duration, and success.
- `GetInertiaPropDiagnostics()` and `GetInertiaRenderDiagnostics()` expose completed request measurements through `HttpContext` extensions.
- Internal `PropDiagnosticsTrace` and `OutputCountingStream` collect measurements.

## Invariants

- Manifest capture inspects registered route metadata without invoking handlers or props factories. Schema version is `1`; output order is stable by name, pattern, and methods.
- Capture/export is opt-in and maps no public endpoint. Export uses indented Web JSON names and omits nulls.
- Diagnostics require `PropDiagnostics.Enabled` and, by default, a Development environment. Disabled access returns an empty prop list or `null` render result.
- Measurements contain classification and timing metadata, never prop values. `MaxEntries` bounds retained measurement slots, including nested props.
- Slow-prop warnings apply to successful resolutions meeting the configured threshold.
- Render bytes count successful application writes before compression; render duration excludes prop resolution. The original response stream is restored in `finally` and remains open.

## Side Effects

Manifest export creates parent directories, writes a temporary JSON file, replaces the target, and cleans up the temporary file. Diagnostics mutate request items, temporarily wrap the response stream, read timing data, and emit logs.

## Verification

Run from the repository root:

```sh
dotnet test tests/InertiaNet.Tests/InertiaNet.Tests.csproj
```

The test project targets `net8.0`, `net9.0`, and `net10.0`.
