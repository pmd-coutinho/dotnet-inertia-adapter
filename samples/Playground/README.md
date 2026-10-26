# InertiaNet playground

One ASP.NET Core backend with React and Vue clients. It demonstrates generated Pathfinder routes and props, typed request DTOs, server validation/error bags, cookie authentication/shared props, flash redirects, deferred totals, scroll merging, native form method overrides, and SSR with a custom root ID.

The store is intentionally in memory and resets when the application restarts. Signing in sets a display-name cookie identity for this local example. No database or external identity provider is required.

## Run

Requires .NET 10 and Node 24.

```sh
cd samples/Playground/ClientApp
npm ci
npm run build
npm run ssr
```

In another terminal:

```sh
dotnet run --project samples/Playground --urls http://localhost:5197
```

Open `/react/todos` or `/vue/todos`. `npm run dev` starts Vite when developing the frontend; rebuild the SSR bundle when changing server-rendered components. Set `SSR_ENABLED=false` for CSR only, or `SSR_URL` to change the default `http://127.0.0.1:13714`. The “Try SSR fallback” link deliberately makes the Node renderer fail while the .NET gateway serves a usable CSR page.

`npm run generate` evaluates the project and emits code into `ClientApp/src/pathfinder`. The contract profile uses string IDs for Int64/UInt64. The generated directory, frontend/SSR bundles and exported endpoint snapshots are ignored by Git.

## Verify

```sh
cd samples/Playground/ClientApp
npm run build
npm run test:manifest
npx playwright install chromium
npm run test:browser
```

The manifest check explicitly starts the application's configuration/mapping code, exports registered metadata, and compares it with source discovery. Ordinary generation does not start the app. Browser tests start their own .NET/Node servers on ports 5197/5198 and exercise both adapters, SSR hydration/fallback/custom roots, actual cookie/header antiforgery validation, validation-only requests without writes, CRUD, flash, authenticated shared props, deferred loading, scroll append/deduplication and native deletion forms. Override ports with `PLAYGROUND_PORT` / `PLAYGROUND_SSR_PORT`. For an existing Chromium installation, set `PLAYWRIGHT_CHROMIUM_EXECUTABLE`.

The MVC antiforgery filter validates unsafe requests. Initial GETs set the `XSRF-TOKEN` cookie; Inertia sends `X-XSRF-TOKEN`, matching ASP.NET configuration. Native forms send the same request token in `__RequestVerificationToken`. Minimal API applications should explicitly validate unsafe JSON endpoints with `IAntiforgery` or appropriate endpoint metadata; a shared token alone does not enforce validation.

## Interface

- Source/configuration: [Program.cs](./Program.cs), [PlaygroundMiddleware.cs](./PlaygroundMiddleware.cs), [TodoRepository.cs](./TodoRepository.cs), [contracts.json](./contracts.json), [Views/App.cshtml](./Views/App.cshtml).
- `Program.cs` configures MVC antiforgery, cookie authentication, singleton `TodoRepository`, `AddInertiaWithSsr<PlaygroundMiddleware>`, Vite and controller routes; `/` redirects `/react/todos`.
- `TodoRepository`: `Count`, `Page`, `Add`, `Update`, `Delete`; records `TodoRow`, `TodoPage`, `TodoStats`, `UserSummary`, `AuthShared` and `AppShared`.
- `PlaygroundMiddleware` shares `xsrfToken` and `auth` per request and `app` as a once prop.
- `PATHFINDER_EXPORT_ENDPOINTS` writes `InertiaEndpointManifest` then returns without `app.Run`.
- `Views/App.cshtml` selects React/Vue asset by the `client` route value and renders `playground-root`.
- [ClientApp](./ClientApp/README.md): Playground frontend generation, dual-adapter builds, SSR service and Playwright orchestration.
- [Controllers](./Controllers/README.md): MVC todo routes, request DTO validation and typed Inertia page props.

## Invariants

- Store has 16 seed rows, monotonic long IDs, 5-row descending-ID pages, locked access and lifetime limited to process.
- `LargeIntegerSerialization.String` matches the `contracts.json` setting `output.largeIntegers: String`; version `playground-v1`.
- `SSR_ENABLED` defaults `true`, `SSR_URL` defaults `http://127.0.0.1:13714`, timeout one second; development enables prop diagnostics.
- `GET` XSRF token stored in `HttpContext.Items` and readable cookie; unsafe MVC requests checked by global antiforgery filter.
- Native `_method` override precedes routing.

## Side Effects

- Starts the HTTP app, reads configuration/environment, serves assets, authenticates via cookies and writes antiforgery cookies, keeps shared in-memory data, makes SSR HTTP requests and optionally writes endpoint manifest.

## Verification

Use the existing [Verify](#verify) instructions above from `samples/Playground/ClientApp`. For a backend compilation check, run `dotnet build samples/Playground/Playground.csproj` from the repository root. Browser verification needs .NET 10, Node 24, built assets and Chromium as described above.
