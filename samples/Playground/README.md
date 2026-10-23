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
