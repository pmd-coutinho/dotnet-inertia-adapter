# Authentication Challenge Handling

## Overview

Opt-in translation of ASP.NET Core authentication challenge and forbid responses into Inertia protocol responses, for cookie, OpenID Connect, and other schemes.

## Interface

- `InertiaChallengeOptions.ApiRequestPredicate` selects non-Inertia requests that receive `401`/`403` instead of redirects. Null disables that handling.
- `InertiaChallengeOptions.DefaultApiRequestPredicate` is the default heuristic. It matches Laravel's `expectsJson()`, plus non-Inertia requests whose `Sec-Fetch-Mode` is present and not `navigate`, plus requests that `InertiaOptions.RequestPredicate` excludes whose `Accept` header does not list HTML.
- The internal `InertiaAuthenticationService` decorates the registered `IAuthenticationService`. `AddInertiaChallengeHandling` on `IServiceCollection` or `AuthenticationBuilder` registers it.

## Invariants

- Authenticate, sign-in, and sign-out calls pass through unchanged. Challenge and forbid invoke the wrapped service first; only its resulting response is translated. The exception is an Inertia `GET` challenge with `Purpose: prefetch`, which is answered with `409` and `X-Inertia-Location` set to the request's own `PathBase + Path + QueryString` without invoking the wrapped service.
- A request is an Inertia request when it carries `X-Inertia` and `InertiaOptions.RequestPredicate` does not exclude it. Other requests are API requests only when the predicate selects them.
- For an Inertia challenge, a redirect, or a `401` carrying `Location` (the cookie handler's XHR response), becomes `409` with `X-Inertia-Location`. `Location` is removed and the response varies on `X-Inertia`. For an API challenge, a redirect or a `401` carrying `Location` becomes `401` without `Location`, and `Set-Cookie` is restored to its value before the wrapped challenge ran. When an Inertia challenge was already translated in the request (a policy with several schemes) and a later scheme answers `401` without `Location`, the status is set back to `409`.
- For an Inertia or API forbid, a redirect or a `403` carrying `Location` becomes `403` without `Location`.
- Apart from that multi-scheme case and prefetches, non-redirect responses and started responses are left unchanged. Nested challenges, from forwarded or remote schemes, therefore translate once.
- A non-GET/HEAD Inertia challenge without `RedirectUri` receives a copied `AuthenticationProperties` whose `RedirectUri` is the local or same-origin `Referer` path and query (rejected unless the result is a local path, see `Support/RefererUrl`), otherwise `PathBase + "/"`. The caller's properties are not mutated.
- Registration is idempotent and independent of the `AddAuthentication()` call order. It wraps the last non-keyed `IAuthenticationService` registration with the same lifetime.

## Side Effects

Changes response status codes and the `Location`, `X-Inertia-Location`, `Vary`, and (for API challenges) `Set-Cookie` headers, and sets a request item marking a translated location visit. Wrapped scheme handlers keep their own side effects, such as correlation and nonce cookies on Inertia and browser challenges; prefetch challenges do not invoke the handler.

## Verification

Run from the repository root:

```sh
dotnet test tests/InertiaNet.Tests/InertiaNet.Tests.csproj
```

The test project targets `net8.0`, `net9.0`, and `net10.0`.
