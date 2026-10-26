# Package audit follow-up

* **Author:** Codex, for the maintainer
* **Date:** 2026-10-26
* **Status:** Implemented and verified. The maintainer requested implementation of the changes that made sense; no formal Goldfish review was performed.

## Overview

Address unintended request effects, inaccurate generated URLs, missing Vite integration options, and weak TempData test behavior. Preserve the existing empty-response redirect default because it is intentional reference-adapter behavior. Provide an opt-out instead of requiring existing applications to opt back in.

## Analysis and decisions

1. **Automatic MVC validation:** Capture redirected ModelState errors only when the request carries `X-Inertia` and matches the configured predicate. Explicitly attached Minimal API validation still supports ordinary form submissions without this header.
2. **Request scope:** Add optional `InertiaOptions.RequestPredicate`. Excluded requests bypass the Inertia middleware before version/sharing hooks, headers, and TempData processing. MVC and Minimal API validation and Precognition filters respect the same predicate. Initial HTML requests remain eligible when their paths match.
3. **Empty responses:** Add `RedirectEmptyResponses`, defaulting to true. Setting it to false preserves empty 200/204 responses while retaining mutation redirect normalization. Precognition remains excluded. Do not change the default to opt-in.
4. **Lazy CSRF tokens:** Register a request-local lazy `xsrfToken`. Generate it only when page prop resolution includes it, after the handler establishes the current identity and before headers start. Redirects and partial reloads excluding the token avoid antiforgery generation effects.
5. **Vite integration:** Prefix production script, stylesheet, and preload URLs with the request's PathBase. Support explicit helper nonces and `ViteOptions.NonceProvider`, including the React refresh preamble. Consume optional build-time integrity metadata using `IntegrityKey`, including matching CSS by output filename. Emit crossorigin with integrity. Keep HMR URLs based on the hot file.
6. **TempData fidelity:** Replace the no-op test dictionary with the framework's TempDataDictionary backed by an in-memory provider, with a helper for advancing to a fresh request. Remove the production middleware's blanket Keep call so application-owned values expire normally. Verify actual cookie-backed request lifecycles as well as Keep/Peek/read/remove behavior.
7. **Pathfinder accuracy:** Resolve supported controller template and name constants with Roslyn, including constants in other source files. Warn with `PATHFINDER102` and omit dependent helpers when a template or metadata cannot be resolved, including conventional actions without an explicit attribute template. Preserve explicit root routes, independent absolute routes, and valid sibling routes. Strict generation retains the previous output on warnings.

## Goals vs. Non-Goals

* **Delivered:** Request boundaries, lazy token generation, Vite PathBase/nonces/integrity, accurate controller route discovery, and realistic transient-state tests.
* **Excluded:** A new public flash Keep API, changing the default empty-response behavior, generating CSP headers/nonces inside the adapter, request-time asset hashing, and attempting to infer conventional route registration.

## Verification

* Runtime suite: 241 tests passed on .NET 10.
* Pathfinder suite: 116 tests passed, including CLI strict-mode preservation and build integration.
* TypeScript suite: generation, type checking, compilation, and 14 runtime/protocol tests passed.
* Runtime and Pathfinder source builds passed for net8.0, net9.0, and net10.0. Only the .NET 10 runtime is installed locally; the source builds verify compilation for the earlier frameworks.
* Git whitespace checks passed.

Real-cookie integration tests cover initial HTML and JSON requests, excluded paths, validation redirects, prefetch, version conflicts, application TempData expiry, and antiforgery validation after the handler changes the identity. Vite tests cover nested imports, CSS/preload integrity, nonce encoding and request isolation, HMR, DI activation, and prefixed URLs.

## Integration boundaries

Applications supply a fresh nonce per response and use it in their CSP header. Vite requires build-generated integrity metadata; absent hashes are optional. Integrity applies to tags emitted by these helpers, not to arbitrary dynamic imports. Configure the frontend build base consistently with PathBase. RequestPredicate scopes adapter behavior, not the application's authentication or antiforgery middleware.

Usage and configuration are documented in [README.md](README.md) and the runtime/Pathfinder directory READMEs.
