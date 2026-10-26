# Protocol Support

## Overview

Shared constants, asset-version hashes, and response-header normalization support the runtime protocol.

## Interface

- `HeaderNames` centralizes Inertia, partial reload, reset, once, scroll, prefetch, redirect, error-bag, and Precognition header names.
- `SessionKeys` defines `ClearHistory`, `FlashData`, `PreserveFragment`, `ValidationErrors`, and `ErrorBag` TempData keys.
- `ManifestVersionStrategy.Hash(path)`, `HashString(value)`, and `FromViteOrMix(webRootPath)` provide version hashes/delegates.
- Internal `InertiaResponseHeaders.AppendVary` appends response variation tokens.

## Invariants

- File and UTF-8 string hashing use xxHash128 with a lowercase 32-character hexadecimal digest. Missing files return `null`.
- `FromViteOrMix` checks `build/manifest.json` before `mix-manifest.json` under the supplied web root each time its delegate runs.
- `AppendVary` preserves existing tokens, deduplicates case-insensitively, and leaves an existing wildcard unchanged.
- Session constants name storage entries; persistence belongs to middleware and filters.

## Side Effects

File hashing reads local file contents and may propagate I/O errors. `AppendVary` mutates response headers. Constant access and string hashing perform no external I/O.

## Verification

Run from the repository root:

```sh
dotnet test tests/InertiaNet.Tests/InertiaNet.Tests.csproj
```

The test project targets `net8.0`, `net9.0`, and `net10.0`.
