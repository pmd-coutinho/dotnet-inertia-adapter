# InertiaNet kitchen sink

A working field guide to Inertia v3 with ASP.NET Core 10, React 19, generated
Pathfinder routes, and a persistent SQLite database. Each demo includes a short
explanation and code example. The persistent layout has a response inspector
showing props, protocol metadata, and recent client events.

## Pathfinder throughout the sample

All application links and explicit visits use generated controller or Minimal
API helpers, including sidebar navigation, query-string filters, deferred retries,
redirect demonstrations, and the full-page SSR links. Inertia links receive
`Demos.contacts()`; native anchors use `Demos.contacts.url()` to keep ordinary
browser navigation. The fragment demonstration appends its fragment to the
generated navigation URL.

Every page consumes `PropsFor<"ComponentName">` from the generated page map.
The browser and SSR component registry is checked against `ComponentName`, so a
new server-rendered component requires a matching client registration. Shared
props come from Pathfinder's Inertia `PageProps` augmentation; there is no separate
handwritten shared-prop or feed-entry interface.

Contact, profile and sign-in forms use generated payload aliases; uploads derive
their selected-file type from the generated form contract. The editing state
allows `null` before a file is selected. Query objects use the generated query
types with `satisfies` to catch misspelled names and incorrect value types.
The upload flash receipt remains a local shape: Pathfinder does not currently
generate flash-data contracts.

```tsx
import Demos, { type DemosStoreContactPayload } from './pathfinder/actions/demos'
import type { PropsFor } from './pathfinder/types/page-map'

const form = useForm<DemosStoreContactPayload>({ name: '', email: '', team: 'Design' })
form.submit(Demos.storeContact(), { errorBag: 'contact' })

function Contacts({ contacts }: PropsFor<'Contacts'>) { /* ... */ }
```

`npm run build` generates the contracts in strict mode before type-checking.
`npm run check:contracts` checks existing generated output without rewriting it.
`tests/pathfinder-contracts.ts` contains compile-time regression cases for DTOs,
query fields/types, upload values, numeric IDs and component names.

## Run with Aspire

Requires .NET 10 and Node 24. Docker is not required: the web app creates and seeds
SQLite automatically. The AppHost uses the [Aspire SDK](https://aspire.dev/get-started/aspire-sdk/)
and [JavaScript hosting](https://aspire.dev/get-started/app-host/).

From the repository root, prepare the frontend once:

```sh
npm ci --prefix samples/KitchenSink/ClientApp
npm run build --prefix samples/KitchenSink/ClientApp
dotnet run --project samples/KitchenSink.AppHost
```

Open the dashboard URL printed by Aspire, then open the **web** resource at
http://localhost:5270. The **vite** resource serves assets; open the web resource
to browse the sample. Aspire starts Vite, the Node SSR renderer, and ASP.NET Core,
injects the renderer URL, and waits for SSR health before starting the web app.

The browser entry uses Vite HMR. Development SSR requests go through Vite's
`/__inertia_ssr` proxy to the Node renderer. The SSR bundle is built when its
resource starts; restart the **ssr** resource after changing page components to
refresh server-rendered HTML. Browser HMR does not rebuild the SSR bundle.

CSS is a separate Vite entry linked from the Razor `<head>` in both development
and production. This lets SSR content paint with its styles before JavaScript
loads; importing the stylesheet only from the browser entry would cause a flash
of unstyled content in development.

## Run without Aspire

Build assets as above. In separate terminals, run:

```sh
npm run ssr --prefix samples/KitchenSink/ClientApp
```

```sh
dotnet run --project samples/KitchenSink --urls http://localhost:5270
```

Open http://localhost:5270. For frontend development, also run
`npm run dev --prefix samples/KitchenSink/ClientApp` (Vite defaults to port 5174).
Set `SSR_ENABLED=false` on the web process for client rendering only. Shut Vite
down cleanly before switching back to built assets; if it was killed abruptly,
remove `samples/KitchenSink/wwwroot/hot` so the backend uses the build manifest.

## Demos

| Page | Functionality |
| --- | --- |
| `/` | Inertia links, persistent layout, `Head` titles/meta, prefetching, overview |
| `/contacts` | SQLite CRUD, generated route helpers and typed page props, query-string search, pagination, partial reloads, replace/preserve state, validation, flash redirects, native `_method` forms |
| `/forms` | `useForm`, dirty/processing state, reset, DataAnnotations validation, isolated error bags, Precognition on blur, multipart uploads, progress and cancellation |
| `/props` | Grouped deferred props and loading fallbacks, rescued failures and retry, optional props, `Always`, page once-props with expiry/custom key, global `ShareOnce`, `WhenVisible` |
| `/merging` | Append, prepend, ID matching/deduplication, deep merge, explicit reset |
| `/feed` | `Scroll` metadata, `InfiniteScroll`, automatic/manual loading, URL synchronization, ID matching |
| `/polling` | Partial polling, pause/resume, stable omitted props, background throttling |
| `/navigation` | `useRemember`, cookie identity/shared auth, conditional sharing, once data, prefetch, history encryption/clearing, fragment preservation, full-location redirects |
| `/rendering` | SSR, hydration, custom root ID, per-request opt-out, gateway failure/CSR fallback, handled render exceptions |
| `/minimal` | The same rendering pipeline from a Minimal API endpoint |
| Every page | Shared props, antiforgery, `OnAfterResolveProps` hook, response inspector and client lifecycle events |

The protocol checks also exercise stale asset-version responses, private prefetch
cache headers, and missing-antiforgery rejection. The existing
[Playground](../Playground/README.md) provides equivalent React and Vue clients.
This sample concentrates the feature collection in React; it is not a second Vue
implementation or a production identity system.

## Database and configuration

- The default database is `App_Data/kitchen-sink.db` under the web project. It is
  ignored by Git, persists across runs, and seeds 24 contacts when empty. All
  visitors share this sample database. Stop the web app and remove `App_Data` to
  start over.
- `DATABASE_PATH` selects another SQLite file. Parent directories are created.
- `SSR_ENABLED` defaults to `true`; `SSR_URL` defaults to
  `http://127.0.0.1:13715`. `SSR_PORT` configures the Node listener. Aspire supplies
  the renderer URL and port automatically.
- The web app uses cookie TempData for flash and redirect validation errors. It
  issues `XSRF-TOKEN` on GET; unsafe MVC requests validate `X-XSRF-TOKEN`, or
  `__RequestVerificationToken` for native forms.
- Signing in sets a display-name cookie. It grants no permissions and creates no
  account. Signing out calls `ClearHistory`.
- Uploaded files are read and discarded, never stored or served. The upload demo
  accepts up to 2 MB. Larger request bodies can be rejected with HTTP 413.
- The intentional error demo throws during prop resolution. `HandleExceptionsUsing`
  handles that render-phase failure; the sample uses `OnStarting` to retain HTTP
  500 because the adapter's normal JSON writer selects 200. Controller exceptions
  outside rendering still require ordinary ASP.NET Core exception handling.
- History encryption needs HTTPS or a browser-trusted localhost context.
- Generated contracts and browser/SSR output are ignored. `npm run build` restores
  and generates from source with Pathfinder strict mode before type-checking.

## Verify

From the repository root:

```sh
dotnet build samples/KitchenSink.AppHost/KitchenSink.AppHost.csproj
npm ci --prefix samples/KitchenSink/ClientApp
npm run build --prefix samples/KitchenSink/ClientApp
cd samples/KitchenSink/ClientApp
npx playwright install chromium
npm run test:browser
```

Stop the AppHost/Vite before browser verification so built-asset and SSR checks
do not use a development hot file. Tests launch their own .NET and Node processes
on ports 5271/5272, use an isolated temporary SQLite database, and remove that
database afterwards. `KITCHEN_SINK_PORT`, `KITCHEN_SINK_SSR_PORT`, and
`PLAYWRIGHT_CHROMIUM_EXECUTABLE` override the test defaults. On NixOS, use a
Chromium executable or shell providing Chromium's shared library dependencies.

The browser suite checks desktop/mobile rendering, all demo routes, SQLite CRUD,
validation-only requests, uploads/error bags, grouped and rescued deferred props,
optional/once/visible props, merge behavior, all five feed pages, polling controls,
remembered state, authentication, history, fragments, SSR fallback, and protocol
headers. It saves overview screenshots under the ignored `test-results` directory.

## Source map

- `Program.cs`: MVC, antiforgery, authentication, Inertia, Vite, database startup
  and Minimal APIs.
- `Controllers/DemosController.cs`: page props and mutations, including a typed
  contacts contract and explicit merged-prop type hints.
- `ContactRepository.cs`: parameterized SQLite access and transactional seeding.
- `KitchenSinkMiddleware.cs`: shared/once/conditional props and render hook.
- `Views/App.cshtml`: Razor shell and the custom `kitchen-sink` root.
- `ClientApp/src/pages`: interactive examples; `Layout.tsx` owns persistent UI
  and the response inspector.
- `ClientApp/src/resolve.tsx`, `app.tsx`, `ssr.tsx`: shared component registry,
  browser hydration, and Node renderer.
- `../KitchenSink.AppHost/AppHost.cs`: local process orchestration.
