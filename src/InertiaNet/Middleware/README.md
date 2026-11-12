# Request Middleware and Validation

## Overview

Request lifecycle middleware connects shared props, asset versions, TempData, redirects, history flags, and MVC/Minimal API validation.

## Interface

- `InertiaMiddleware` provides `InvokeAsync` and protected virtual `Share`, `ShareOnce`, `GetVersion`, `GetRootView`, `OnVersionChange`, `OnEmptyResponse`, `OnRedirectWithFragment`, and `ShareCsrfToken` hooks.
- `EncryptHistoryMiddleware` and the MVC `[EncryptHistory]` attribute enable request-level history encryption.
- `InertiaPrecognitionFilter` and `InertiaPrecognitionEndpointFilter` handle validation-only MVC and Minimal API requests.
- `InertiaValidationFilter` and `InertiaValidationEndpointFilter` forward errors across redirects.
- Internal `PrecognitionValidation` supplies recursive DataAnnotations validation, field selection, and response headers.
- Internal `ValidationErrorKeys` normalizes error keys for both validation forwarding and Precognition.

## Invariants

- Root view and version are resolved once into request-local settings, using subclass overrides before option defaults. Responses vary on `X-Inertia`.
- A GET Inertia version mismatch with a non-null server version returns `409` with `X-Inertia-Location` before consuming forwarded state or running the handler. The location is the original request URL, also when the check runs while an error route is re-executed. Protocol `409` responses from the version and fragment hooks disable status code pages for the request, so `UseStatusCodePages` cannot replace them.
- An always-included empty `errors` prop is shared before custom sharing/CSRF hooks. TempData support is optional; missing support logs a warning.
- Prefetch requests skip middleware TempData restoration, consumption, and persistence. Application hooks and antiforgery services retain their own behavior.
- Ordinary visits restore forwarded errors, flash, and history flags and consume their entries. Redirects persist accumulated flash/history state when TempData is available.
- `InertiaOptions.RequestPredicate` optionally scopes the middleware and validation/Precognition filters. Excluded requests skip middleware hooks, shared props, headers, version checks, and TempData processing. Initial HTML requests must remain included.
- Unstarted empty Inertia responses invoke the empty-response hook when `RedirectEmptyResponses` is enabled (the default), except Precognition. Mutation-method `302` redirects become `303`; eligible fragment redirects become `409` with `X-Inertia-Redirect`.
- Default empty-response redirects accept safe relative or same-origin Referer URLs, otherwise use the current request URL. The resulting path must be local (`/` not followed by `/` or `\`, no control characters), so a same-origin Referer such as `http://host//evil.example` falls back to the current URL.
- `Precognition: true` skips the action/endpoint body and returns `204` for valid input or `422` with selected errors. Headers vary on Precognition and confirm successful validation when valid.
- Minimal API Precognition validates bound non-service arguments with DataAnnotations, avoids cycles, and bounds recursion. Precognition error paths are always dot paths; validate-only selection includes related parent/child paths and compares fields segment by segment, ignoring case and bracket-or-dot indexer spelling, so every returned key selects itself. Internal `PrecognitionErrorResult` writes the 422 body (`{ message, errors }`) by hand for both MVC and Minimal APIs, so the application's MVC or Minimal API JSON options rename neither the envelope nor the already-normalized error keys.
- Error keys are normalized per path segment: member names use the property naming policy props are serialized with (camelCase by default; custom `JsonSerializerOptions` with a `null` policy keep CLR names), numeric indexers are kept, other indexers use the dictionary key policy, a leading `$` JSON path root is removed, and colliding keys merge their messages. Forwarded errors follow `ValidationErrorKeyFormat` (`Dots` by default, matching Precognition; `Brackets`; or `Preserve`), and restored error and error-bag keys are literal, so `DictionaryKeyPolicy` does not rename them again.
- MVC validation forwarding requires `X-Inertia`, invalid ModelState, and a recognized redirect result. Minimal API forwarding requires explicitly stored errors and a recognized redirect result; both support error bags and the configured request predicate.

## Side Effects

Mutates response status/headers, request items, shared props, history flags, and TempData. Invokes application hooks, validators, property getters, and the downstream pipeline. The default CSRF hook registers a lazy `xsrfToken`; `IAntiforgery.GetAndStoreTokens` runs only if prop resolution includes it. Automatic MVC error forwarding requires `X-Inertia`; explicitly opted-in Minimal API validation also supports ordinary form requests. Middleware removes consumed Inertia TempData keys without retaining unrelated application entries.

## Verification

Run from the repository root:

```sh
dotnet test tests/InertiaNet.Tests/InertiaNet.Tests.csproj
```

The test project targets `net8.0`, `net9.0`, and `net10.0`.
