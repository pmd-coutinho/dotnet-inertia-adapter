# Playground React page

## Overview

Typed React todo page exercising InertiaNet/Pathfinder integration.

## Interface

- Source/configuration: [Todos.tsx](./Todos.tsx).
- `Todos.tsx` default export `TodoIndex` uses `PropsFor<'React/Todos'>`, generated `Todos` actions, `TodosStorePayload`, `TodosLoginPayload` and `formFields`.
- UI supports sign-in, create/update, Inertia delete, native delete, partial load-more, flash and deferred statistics.

## Invariants

- Generated `ClientApp/src/pathfinder` must exist before typecheck/build.
- Todo IDs are strings in the client contract.
- Native forms include method-override fields from `formFields` and `__RequestVerificationToken`.
- Create/update form uses the `todo` error bag.

## Side Effects

- Sends HTTP requests through Inertia forms/router and native forms; mutates local editing/form state; renders page DOM.

## Verification

Working directory: `samples/Playground/ClientApp`. Requires .NET 10 and Node 24; the build creates the generated Pathfinder contracts and browser/SSR bundles.

```sh
npm ci
npm run build
npm run test:manifest
npx playwright install chromium
npm run test:browser
```

Playwright starts its own .NET and Node servers. Override their ports with `PLAYGROUND_PORT` and `PLAYGROUND_SSR_PORT`; an installed browser can be selected with `PLAYWRIGHT_CHROMIUM_EXECUTABLE`.
