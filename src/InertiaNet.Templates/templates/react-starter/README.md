# React starter

## Overview

ASP.NET Core .NET 10 InertiaNet React project template.

## Interface

- Source/configuration: [Program.cs](./Program.cs), [InertiaNetStarter.csproj](./InertiaNetStarter.csproj), [.template.config/template.json](./.template.config/template.json), [Views/App.cshtml](./Views/App.cshtml).
- `Program.cs` registers MVC with `AutoValidateAntiforgeryTokenAttribute`, `X-XSRF-TOKEN` antiforgery header, Inertia root `/Views/App.cshtml` and page existence checking in `ClientApp/src/pages`.
- `GET /` maps `Home`; `MapInertiaFallback("Home")` handles fallback.
- Vite helper uses `wwwroot/build/manifest.json` and `hot` file.
- `Views/App.cshtml` renders `inertia-head`, `inertia` root and `src/app.tsx` entry; `_ViewImports` registers InertiaNet tag helpers.
- `.template.config/template.json` declares identity `InertiaNet.Templates.ReactStarter`, short name `inertianet-react`, source name `InertiaNetStarter`, and `preferNameDirectory: true`.
- [ClientApp](./ClientApp/README.md): React starter frontend package, compiler configuration, and Vite build boundary.

## Invariants

- Middleware order: static files, `_method` form override, routing, XSRF cookie middleware, antiforgery, Inertia.
- `GET` responses issue readable `XSRF-TOKEN` cookies with `SameSite=Lax`, request-dependent `Secure` and path `/`.
- `InertiaNetStarter.csproj` contains `__INERTIANET_PACKAGE_VERSION__`; release staging substitutes the shared version.

## Side Effects

- Runs an HTTP server, serves assets, renders responses and sets antiforgery cookies.

## Verification

Working directory: `src/InertiaNet.Templates/templates/react-starter/ClientApp`. Install frontend dependencies, then use the configured type check and Vite build:

```sh
npm install
npm run typecheck
npm run build
```

Use the [template package verification](./../../README.md#verification) for staging, packaging, installation and .NET smoke builds. The unprocessed starter project contains a package-version placeholder.
