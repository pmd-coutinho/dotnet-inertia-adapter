# InertiaNet.Pathfinder

Type-safe route generation for ASP.NET Core + Inertia.js applications. Scans your C# source code with Roslyn and generates TypeScript route helpers, form helpers, page prop types, model interfaces, and enum definitions.

Pathfinder is currently **route-generation-first**:

- Stable core: route helpers, named routes, query support, URL defaults, `.form()` helpers, and runtime parameter validation.
- Experimental: page prop types, model generation, enum generation, and Wolverine discovery.

Pathfinder is also **named-endpoint-first** for long-term .NET stability:

- Prefer `[HttpGet(Name = "Users.Show")]`, `[Route(Name = "...")]`, or Minimal API `.WithName("...")`
- Treat generated named routes as the durable contract between backend routing and frontend navigation
- Use controller/action file generation as a convenience layer, not the only stable integration point

Inspired by [Laravel Wayfinder](https://github.com/laravel/wayfinder).

**Targets:** .NET 8, 9, and 10. Project evaluation requires an SDK that supports the selected project framework.

---

## Features

### Stable

- **Route discovery** from MVC controllers and Minimal APIs
- **TypeScript action helpers** with `{ url, method }` return type and per-method variants
- **Wayfinder-compatible output** — generated helpers can be passed directly to Inertia `Link`, `Form`, `router.visit()`, and `useForm().submit()`
- **Form helpers** (`.form()`) with automatic HTML method spoofing for PUT/PATCH/DELETE
- **Named routes** generated from `[Name = "..."]` attributes and `.WithName("...")`
- **Query parameter support** with `query` and `mergeQuery` options, including nested objects
- **URL defaults** with `setUrlDefaults()` and `addUrlDefault()` for locale prefixes and global parameters
- **Runtime parameter validation** — throws descriptive errors for missing required route parameters
- **JSDoc `@see` comments** pointing to source controller file and line for IDE navigation
- **Naming conflict detection** — disambiguates duplicate export names with numeric suffixes
- **Route constraints** parsed into TypeScript types (`{id:int}` becomes `number`)
- **Barrel files** (`index.ts`) auto-generated for clean imports
- **Watch mode** (`--watch`) with file system watcher and debounce
- **Skip patterns** to exclude controllers/routes from generation

### Experimental

- **Page props type generation** from `Inertia.Render()` calls — typed interfaces per component
- **Model/entity type generation** for classes referenced in page props
- **C# enum to TypeScript** const objects with union types
- **Wolverine endpoint discovery**

---

## Installation

### As a .NET global tool

```bash
dotnet tool install --global InertiaNet.Pathfinder --prerelease
```

### As a local tool

```bash
dotnet tool install InertiaNet.Pathfinder --prerelease
```

---

## Quick Start

### 1. Run Pathfinder

```bash
# Scan the current directory and output to ./pathfinder
pathfinder

# Scan a specific project
pathfinder -p ./src/MyApp -o ./src/frontend/pathfinder

# Select evaluated inputs for one framework/configuration
pathfinder -p ./src/MyApp/MyApp.csproj --framework net10.0 -c Release

# Fail CI for stale output, or incomplete discovery
pathfinder -p ./src/MyApp --check --quiet
pathfinder -p ./src/MyApp --strict --diagnostics-json

# Use only the stable route-generation surface
pathfinder --models false --enums false --props false --wolverine false
```

When a project is supplied, Pathfinder uses its evaluated `Compile` items, linked files, framework symbols, implicit usings, and configuration. Excluded source files stay excluded. For multi-target projects it selects the first declared framework unless `--framework` is set. A directory without a project uses source scanning; a directory containing several projects requires an explicit `.csproj`.

Diagnostics are written to stderr even with `--quiet`. `--strict` fails before writing output if discovery reports any warning. `--check` compares staged output without modifying files. Exit codes are `0` for success/current output, `1` for stale output or discovery failures, and `2` for invalid arguments, project evaluation or output failures. `--diagnostics-json` emits a JSON diagnostic array with `code`, `file`, `line`, `message`, and `severity`.

Controller attribute templates and route names support string literals and
resolvable compile-time string constants, including constants from other source
files. If a template or required route metadata cannot be resolved, Pathfinder
reports `PATHFINDER102` with its source location and omits the dependent helper.
An action with only `[HttpGet]` and no class attribute template also produces this
warning because conventional routing cannot be inferred. Explicit `/` and empty
attribute templates still generate root routes. Use `--strict` to fail on these
warnings while preserving the last generated output.

Generated output is staged before committing. Pathfinder tracks owned paths in `.pathfinder-manifest.json`, preserves unrelated files, removes only stale owned files, and keeps unchanged modules' timestamps. Commit failures restore prior file content and timestamps. Keep `.pathfinder-files`, `.pathfinder-inputs`, and `.pathfinder.stamp` with the generated directory for build integration. Existing conflicting files are protected; use `--adopt` once when migrating a legacy Pathfinder output directory, after checking that the conflicting paths are generated files. `--no-clean` retains stale owned files.

### 2. Import and use in your frontend

```typescript
import { posts as Posts } from './pathfinder/actions'

// Navigate with Inertia
router.visit(Posts.index())
// → { url: "/posts", method: "get" }

// Get just the URL
const url = Posts.show.url({ id: 42 })
// → "/posts/42"

// Use with Inertia forms
const { submit } = useForm({ title: '', body: '' })
submit(Posts.store())
// → { url: "/posts", method: "post" }

// HTML form with method spoofing
const formAttrs = Posts.update.form({ id: 42 })
// → { action: "/posts/42", method: "post", data: { _method: "PUT" } }
```

---

## CLI Reference

```
pathfinder [options]
```

| Option | Short | Default | Description |
|---|---|---|---|
| `--project <path>` | `-p` | `.` | Path to the .NET project to scan |
| `--output <path>` | `-o` | `./pathfinder` | Output directory for generated files |
| `--framework <tfm>` | | First declared | Target framework to evaluate |
| `--configuration <name>` | `-c` | `Debug` | Project configuration |
| `--contracts <json>` | | | Explicit input/output serialization profiles |
| `--endpoint-manifest <json>` | | | Registered endpoint snapshot to compare |
| `--actions <bool>` | | `true` | Generate action files |
| `--routes <bool>` | | `true` | Generate named route files |
| `--forms <bool>` | | `true` | Generate `.form()` helpers on actions and routes |
| `--models <bool>` | | `true` | Generate experimental model declarations |
| `--enums <bool>` | | `true` | Generate experimental enum declarations |
| `--props <bool>` | | `true` | Generate experimental page/shared prop declarations |
| `--wolverine <bool>` | | `true` | Enable experimental Wolverine discovery |
| `--skip <patterns>` | | | Semicolon-separated skip patterns (e.g. `*Health*;Admin*`) |
| `--clean <bool>` | | `true` | Remove stale Pathfinder-owned files |
| `--no-clean` | | | Shorthand for `--clean false` |
| `--watch` | `-w` | `false` | Watch for file changes and regenerate |
| `--debounce <ms>` | | `300` | Debounce delay for watch mode (milliseconds) |
| `--quiet` | `-q` | `false` | Suppress progress output; diagnostics remain on stderr |
| `--strict`, `--fail-on-warning` | | `false` | Stop before writing if discovery reports diagnostics |
| `--check` | | `false` | Check generated output freshness without modifying it |
| `--adopt` | | `false` | Adopt matching paths in legacy generated output |
| `--diagnostics-json` | | `false` | Emit structured diagnostics on stderr |
| `--version` | | | Print the tool version |
| `--help` | `-h` | | Show supported options |

---

## Generated Output

Pathfinder generates the following directory structure:

```
pathfinder/
├── index.ts              # Runtime types, queryParams, URL defaults, validation
├── actions/              # Route action helpers grouped by controller
│   ├── posts.ts
│   ├── users.ts
│   └── index.ts          # Barrel: re-exports all controllers
├── routes/               # Named routes (from [Name = "..."])
│   ├── api/
│   │   └── index.ts
│   └── index.ts
├── enums/                # C# enums as TypeScript const objects
│   ├── OrderStatus.ts
│   └── index.ts
├── types/                # Page prop interfaces from Render() calls
│   ├── Posts.Index.ts
│   └── index.ts
└── models/               # Entity/model interfaces
    ├── Post.ts
    ├── User.ts
    └── index.ts
```

Action files stay flat for the common case (`PostsController` -> `actions/posts.ts`).
If two controllers would otherwise generate the same file name, Pathfinder keeps namespace segments in the path to avoid collisions (for example `Admin.PostsController` -> `actions/admin/posts.ts`).

---

## Route Discovery

Pathfinder discovers routes from three ASP.NET Core routing paradigms:

### Currently Supported Patterns

- MVC controllers using attribute routes with string-literal templates
- Minimal APIs using `MapGet` / `MapPost` / `MapPut` / `MapPatch` / `MapDelete`, static `MapMethods` method lists, and `MapInertia`
- Minimal API route templates resolved from string literals, constants, and simple interpolated/concatenated static strings
- Minimal API groups assigned to variables via `var posts = app.MapGroup("/posts")`
- Minimal API inline `MapGroup(...)` chains when every prefix segment can be resolved statically
- Minimal API lambda handlers (`() => ...`, `(id) => ...`, `(id, [FromBody] dto) => ...`)
- Source method-group handlers across evaluated files/project references when semantic resolution identifies one method

### Explicitly Unsupported For Now

- Minimal API route templates or group prefixes that depend on runtime values or other non-resolvable expressions
- Dynamic delegate variables, ambiguous overloads and handlers without discoverable source declarations

Unsupported patterns are skipped with a warning instead of generating partial or misleading output.

GET endpoints advertise GET only; use explicit HEAD or OPTIONS registrations when needed. Constrained optional/default parameters, catch-alls, and `[FromRoute(Name = "...")]` aliases use ASP.NET route-pattern semantics. Ordinary path parameters are encoded; `*` encodes slashes while `**` preserves them.

### MVC Controllers

```csharp
[Route("api/[controller]")]
public class PostsController : Controller
{
    [HttpGet]
    public IActionResult Index() => this.Inertia("Posts/Index", new { posts });

    [HttpGet("{id:int}")]
    public IActionResult Show(int id) => this.Inertia("Posts/Show", new { post });

    [HttpPost]
    public IActionResult Store([FromBody] CreatePostRequest request) { ... }

    [HttpPut("{id:int}")]
    public IActionResult Update(int id, [FromBody] UpdatePostRequest request) { ... }

    [HttpDelete("{id:int}")]
    public IActionResult Destroy(int id) { ... }
}
```

Generates `pathfinder/actions/posts.ts`. Import its functions or the controller's default export:

```typescript
import Posts, { show } from './pathfinder/actions/posts'

show(42)                 // { url: '/api/posts/42', method: 'get' }
Posts.update.form(42)     // { action: '/api/posts/42', method: 'post', data: { _method: 'PUT' } }
```

The generated implementation shares its route builder with named routes. Golden files in `tests/InertiaNet.Pathfinder.Tests/Snapshots` show exact output; `tests/InertiaNet.Pathfinder.TypeScript` compiles and executes it against supported Inertia clients.

### Minimal APIs

```csharp
const string ApiPrefix = "/api";
const string PostsPrefix = "/posts";
const string ShowTemplate = "/{id:int}";

var posts = app.MapGroup(ApiPrefix).MapGroup(PostsPrefix);
posts.MapGet("/", () => TypedResults.Ok()).WithName("Posts.Index");
posts.MapGet(ShowTemplate, (int id) => TypedResults.Ok());
posts.MapPost("/", ([FromBody] CreatePostRequest request) => TypedResults.Ok());
```

Generates `pathfinder/actions/minimalApi.ts` with the same helper format.

### Wolverine Endpoints

```csharp
public static class PostEndpoints
{
    [WolverineGet("api/posts")]
    public static Task<IEnumerable<Post>> GetAll() { ... }

    [WolverinePost("api/posts")]
    public static Task<Post> Create(CreatePostCommand command) { ... }
}
```

---

## Action Helpers

Every generated action provides:

| Property | Description |
|---|---|
| `action()` | Returns `{ url, method }` — pass directly to `router.visit()` or `useForm().submit()` |
| `action.url()` | Returns the URL string only |
| `action.definition` | Static route metadata: `{ methods, url }` |
| `action.get()` / `.post()` / etc. | Per-method helpers with `method` typed as const |
| `action.form()` | Returns `{ action, method, data? }` for HTML `<form>` attributes |

If a request payload type can be discovered and generated, `action.body` is typed to that generated model. If Pathfinder cannot resolve the body type safely, it falls back to `unknown` instead of emitting an invalid TypeScript identifier.

### Parameter Formats

Actions with parameters accept flexible input formats:

```typescript
// Single parameter — all equivalent:
show(42)
show([42])
show({ id: 42 })

// Multiple parameters:
edit({ postId: 1, commentId: 5 })
edit([1, 5])
```

### Query Parameters

```typescript
// Add query parameters
index({ query: { page: 2, sort: 'title' } })
// → /api/posts?page=2&sort=title

// Merge with existing query params
index({ mergeQuery: { page: 3 } })

// Array query params
index({ query: { tags: ['news', 'tech'] } })
// → /api/posts?tags=news&tags=tech

// Nested object query params
index({ query: { filter: { status: 'active', sort: 'date' } } })
// → /api/posts?filter.status=active&filter.sort=date
```

Queries use ASP.NET-friendly repeated array keys, dot-separated object paths, and `true`/`false` booleans. Set `arrayFormat: 'brackets' | 'indices'` or `objectFormat: 'brackets'` explicitly when targeting a different binder. `mergeQuery` replaces a whole scalar/array/object parameter family; null removes it.

---

## Form Helpers

The `.form()` method returns `{ action, method, data? }` for HTML form attributes. For HTTP methods not supported by HTML forms (PUT, PATCH, DELETE), it automatically uses method spoofing — sending as POST with `_method` in the `data` field:

```typescript
import { posts as Posts } from './pathfinder/actions'

// GET/POST routes — no spoofing needed
Posts.index.form()
// → { action: "/api/posts", method: "get" }

Posts.store.form()
// → { action: "/api/posts", method: "post" }

// PUT/PATCH/DELETE routes — automatic method spoofing
Posts.update.form({ id: 42 })
// → { action: "/api/posts/42", method: "post", data: { _method: "PUT" } }

Posts.destroy.form({ id: 42 })
// → { action: "/api/posts/42", method: "post", data: { _method: "DELETE" } }
```

Include `data._method` as a hidden input; spreading the returned object alone does not submit that field. Enable form-field override explicitly **before routing**:

```csharp
app.UseHttpMethodOverride(new HttpMethodOverrideOptions { FormFieldName = "_method" });
app.UseRouting();
```

ASP.NET's default override configuration uses a header, not `_method`. Its built-in middleware does not read a `_method` query parameter. `formFields(formDefinition)` returns `{ name, value }[]` for rendering the hidden inputs. Direct Inertia requests should use `action()` / `action.patch()` with their real HTTP method.

Use with Inertia's `useForm`:

```tsx
const form = useForm({ title: '', body: '' })

// Submit directly
form.submit(Posts.store())

// Or spread into a <form> element with a hidden _method field
const formDef = Posts.update.form({ id: 42 })
<form action={formDef.action} method={formDef.method}>
  {formDef.data?._method && <input type="hidden" name="_method" value={formDef.data._method} />}
  {/* inputs */}
</form>
```

Form helpers can be disabled with `--forms false` if not needed.

---

## URL Defaults

URL defaults let you set global parameter values (e.g., locale prefixes) that are automatically applied to all generated URLs. Explicitly provided parameters always override defaults.

```typescript
import { setUrlDefaults, addUrlDefault } from './pathfinder'

// Set all defaults at once
setUrlDefaults({ locale: 'en' })

// Or add individual defaults
addUrlDefault('locale', 'en')

// Lazy evaluation (called on each URL generation)
setUrlDefaults(() => ({ locale: getCurrentLocale() }))
```

When a route template contains `{locale}` and no explicit value is provided, the default will be used:

```typescript
// Route template: /{locale}/posts/{id}
Posts.show({ id: 42 })
// → /en/posts/42  (locale filled from defaults)

Posts.show({ id: 42, locale: 'fr' })
// → /fr/posts/42  (explicit value overrides default)
```

---

## Parameter Validation

Pathfinder validates required route parameters at runtime. If a required parameter is missing, it throws a descriptive error instead of silently producing a malformed URL:

```typescript
// Route template: /posts/{id}
Posts.show({})
// Throws: "Missing required parameter 'id' for route 'show' (/posts/{id})."

// Multiple missing parameters
Posts.comment({})
// Throws: "Missing required parameters 'id', 'slug' for route 'comment' (/posts/{id}/comments/{slug})."
```

Optional parameters (marked with `?` in the route template or with a default value in C#) are not required, but when you omit them they must be omitted from the end of the argument list.

---

## Named Routes

Routes with a `Name` attribute are generated in the `routes/` directory:

```csharp
[HttpGet("{id}", Name = "Posts.Show")]
public IActionResult Show(int id) { ... }

// Minimal APIs
app.MapGet("/posts/{id}", handler).WithName("Posts.Show");
```

```typescript
import { posts as Posts } from './pathfinder/routes'

Posts.show(42)
// → { url: "/posts/42", method: "get" }
```

---

## Page Props Types

Pathfinder analyzes `Inertia.Render()` and `inertia.Render()` calls to generate TypeScript interfaces for your page component props.

### Typed prop classes (recommended)

```csharp
public class DashboardProps
{
    public string UserName { get; set; }
    public int TotalPosts { get; set; }
    public List<Post> RecentPosts { get; set; }
    public Dictionary<string, int>? Stats { get; set; }
}

// In your controller:
inertia.Render<DashboardProps>("Dashboard", new DashboardProps { UserName = "Ana", RecentPosts = [] });
```

Generates `pathfinder/types/Dashboard.ts`:

```typescript
export interface DashboardProps {
    userName: string
    totalPosts: number
    recentPosts: Post[]
    stats?: Record<string, number>
}
```

### Anonymous objects

```csharp
// posts is a typed collection, for example Post[].
inertia.Render("Posts/Index", new
{
    Posts = posts,
    TotalCount = count,
});
```

Generates `pathfinder/types/Posts.Index.ts`:

```typescript
export interface PostsIndexProps {
    posts: Post[]
    totalCount: number
}
```

Anonymous objects use semantic property types when references are available. Typed props, records, inherited/partial declarations, closed generics, referenced projects and restored package DTOs share the same contract pipeline. Unsupported dynamic expressions produce diagnostics and `unknown`.

### Component maps and shared props

Import `PropsFor` from `pathfinder/types/page-map` to select props by the actual component name:

```typescript
import type { PropsFor, ComponentName } from './pathfinder/types/page-map'
type DashboardPage = PropsFor<'Dashboard'>
const component: ComponentName = 'Dashboard'
```

Page interfaces include an unknown index signature for Inertia's `PageProps` constraint. In Vue `defineProps`, import the concrete generated interface (`types/Posts.Index`); `PropsFor` is suitable for TypeScript and `usePage` but its generic indexed-access map exceeds Vue's runtime props compiler.

`Share(key, value)`, `ShareOnce` and `ShareProps(dto)` contribute inferred types to `types/shared.ts`. It augments `@inertiajs/core`'s `PageProps` and is included by the generated type barrel/map. Shared fields are optional because discovery does not prove that every request executes a sharing call. API dot paths become nested fields; literal JSON property names containing dots remain literal. Alternative render shapes for the same component become unions of field types with optional fields where needed.

### Serialization profiles

Default output matches InertiaNet's camel-case names and null omission. Default JSON input matches ASP.NET's Web JSON options, including reading numbers from strings. Explicit nullability and property omission are tracked separately. `JsonPropertyName`, `JsonIgnore`, `JsonInclude`, extension data, number handling and supported enum converters are read from symbols. Unknown converters/polymorphic shapes produce `unknown` with diagnostics; they do not invent a CLR-shaped JSON contract.

Custom application settings need a checked-in contract file. Pathfinder does not execute the application's dependency injection or configuration code:

```json
{
  "output": {
    "naming": "snake_case",
    "ignoreCondition": "WhenWritingNull",
    "stringEnums": true,
    "enumNaming": "camelCase",
    "enumAllowIntegers": false,
    "largeIntegers": "BigInt",
    "typeOverrides": { "MyApp.Contracts.Token": "string" }
  },
  "input": {
    "naming": "camelCase",
    "numberHandling": "AllowReadingFromString"
  }
}
```

```bash
pathfinder -p ./MyApp.csproj --contracts ./contracts.json
```

The file path is relative to the working directory. Use `PathfinderContractFile` in MSBuild. Both build and watch track file changes. Invalid/unknown settings fail generation. Supported naming policies are `preserve`, `camelCase`, `snake_case`, `SNAKE_CASE`, `kebab-case` and `KEBAB-CASE`. Profiles also accept `includeFields`, `ignoreReadOnlyProperties`, `ignoreReadOnlyFields` and `numberHandling`. Overrides are bounded scalar unions (`string`, `number`, `boolean`, `bigint`, `null`, `unknown`) keyed by fully qualified CLR type names and scoped independently to output/input. They describe an application converter; they do not install it.

`largeIntegers` matches InertiaNet's `LargeIntegerSerialization`: `Number` (default), `String`, or `BigInt`. Marker mode generates `number | bigint` for Int64/UInt64/BigInteger and exact bigint enum constants for unsafe values. It requires a BigInt-capable Inertia client (tested 3.9.1). String mode supports the older tested client 3.0.2. Numeric enum contracts, JSON input enum contracts and route/query/form enum contracts remain independent; ASP.NET binds route/query/form enum names or numeric text regardless of output JSON converters.

### Request contracts and validation

Explicit `[FromBody]`, `[FromQuery]` and `[FromForm]` parameters produce separate payload/query/form types exposed through generated aliases and `.body`, `.query`, `.formData` type carriers. JSON input uses JSON names; query/form input uses native binding names. Dedicated DTOs avoid conflicting binding-source attributes on one property. Query/form contracts exclude properties bound from another source and `[BindNever]`. Files map to `File | Blob` in multipart contracts.

Request DTOs also export `<TypeName>Validation` metadata for Required, length, range, email, URL and regular-expression attributes. .NET patterns retain `patternLanguage: 'dotnet'`; they are not silently treated as JavaScript expressions. These are hints, not client validation enforcement. Server validation remains authoritative. Custom attributes produce diagnostics. Implicit body/service inference, dynamic validators and custom binding conventions still require an explicit contract and remain experimental. `BigInteger` JSON input needs an application converter plus an explicit input type override; InertiaNet's large-integer option configures page output independently of ASP.NET request JSON.

Module paths, runtime helper names, payload aliases and validation exports are allocated independently. Colliding controller paths receive deterministic suffixes; punctuation in named routes is sanitized. Barrels retain every namespace when a file and directory share a name. Custom CLR types and attributes are identified by their symbols rather than mistaken for framework types with the same short name.

### Supported type mappings for props

| C# Type | TypeScript Type |
|---|---|
| `string`, `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `Uri` | `string` |
| `int`, `short`, `byte`, `float`, `double`, `decimal` | `number` |
| `long`, `ulong`, `BigInteger` | `number`, `string`, or `number \| bigint`, according to the output profile |
| `bool` | `boolean` |
| `object` | `unknown` |
| `T?` / `Nullable<T>` | `T \| null` when written; optional `T` when nulls are omitted |
| `byte[]` | Base64 `string` |
| `List<T>`, `IEnumerable<T>`, other `T[]` | `T[]` |
| `Dictionary<K, V>` | `Record<string, V>` (JSON object keys are strings) |
| `Func<T>`, `Lazy<T>`, `Task<T>`, `ValueTask<T>` | Unwrapped to `T` |
| Registered enums | Enum type name |
| Other classes | Referenced by name (generates model) |

---

## Model Types

Classes referenced in page props are automatically discovered and generated as TypeScript interfaces.

```csharp
public class Post
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string Body { get; set; }
    public DateTime CreatedAt { get; set; }
    public User? Author { get; set; }
    public List<Tag> Tags { get; set; }
}
```

Generates `pathfinder/models/Post.ts`:

```typescript
export interface Post {
    id: number
    title: string
    body: string
    createdAt: string
    author?: User
    tags: Tag[]
}
```

Models are generated for referenced page/shared/request contracts rather than every class. Output uses interfaces; request contracts use type aliases compatible with Inertia's request payload declarations. Symbol identities disambiguate same-named DTOs/enums with deterministic suffixes, including constructed generic contracts with different argument nullability.

---

## Enum Types

C# enums are discovered and generated as TypeScript const objects with derived union types.

```csharp
public enum OrderStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
}
```

Generates `pathfinder/enums/OrderStatus.ts`:

```typescript
export const OrderStatus = {
    Pending: 0,
    Approved: 1,
    Rejected: 2,
} as const

export type OrderStatus = (typeof OrderStatus)[keyof typeof OrderStatus] | number
```

Enums are numeric by default, including implicit, negative, and computed constants. A type-level string-enum converter opts into quoted values:

```csharp
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Color
{
    Red,
    Green,
    Blue,
}
```

```typescript
export const Color = {
    Red: "Red",
    Green: "Green",
    Blue: "Blue",
} as const

export type Color = (typeof Color)[keyof typeof Color] | number
```

Numeric enums allow undefined numeric values, matching .NET serialization. String converters that allow integers retain the numeric alternative; disabling integers in the explicit profile removes it. Flags can produce combined strings. `JsonStringEnumMemberName` is honored only when present in the selected framework's reference assemblies. Separate input/query/form enum exports prevent output converters from changing endpoint binding contracts.

---

## Route Constraints

Route template constraints are parsed and mapped to TypeScript types:

| Constraint | TypeScript Type |
|---|---|
| `{id:int}`, `{id:long}`, `{id:float}`, `{id:double}`, `{id:decimal}` | `number` |
| `{slug:alpha}`, `{name:minlength(3)}`, `{code:maxlength(10)}` | `string` |
| `{active:bool}` | `boolean` |
| `{key:guid}` | `string` |
| `{id:min(1)}`, `{id:max(100)}`, `{id:range(1,100)}` | Uses the bound C# parameter type; these constraints alone do not declare a CLR type |
| `{id?}` (optional) | Parameter marked optional |

Constraints are stripped from the generated URL template — `{id:int}` becomes `{id}` in the output.

---

## Optional registered-endpoint validation

Export the application's actual endpoint metadata after mapping routes. This is an explicit application operation; Pathfinder never starts the app as part of ordinary generation:

```csharp
using InertiaNet.Diagnostics;

// After MapControllers/MapGet/MapInertia and before Run:
if (Environment.GetEnvironmentVariable("PATHFINDER_EXPORT_ENDPOINTS") is { } path)
{
    await InertiaEndpointManifest.Capture(app).WriteAsync(path);
    return;
}
```

```bash
PATHFINDER_EXPORT_ENDPOINTS=./endpoints.json dotnet run --project MyApp.csproj
pathfinder -p MyApp.csproj --endpoint-manifest ./endpoints.json --strict --check
```

The versioned snapshot contains registered names, HTTP methods, route patterns/defaults, hosts and available component metadata. `MapInertia` supplies component metadata automatically; other handlers can opt in with `.WithInertiaComponent("Posts/Index")`. Exporting enumerates `EndpointDataSource` without invoking request handlers/props factories. Application startup/configuration can still require infrastructure, so run it deliberately in a configured environment.

Pathfinder reports source endpoints missing from registration, conflicting metadata, and registered named endpoints missing from source. Unnamed framework infrastructure endpoints do not require generated helpers. `--strict` makes differences fail without replacing last-good output. Skip patterns apply to manifest validation too. MSBuild accepts `PathfinderEndpointManifest`; both build/watch track changes to the snapshot. Keep the snapshot outside the generated output directory. It is a local artifact; no HTTP manifest endpoint is exposed.

## Watch Mode

Use `--watch` to automatically regenerate when C# files change:

```bash
pathfinder -p ./src/MyApp -o ./src/frontend/pathfinder --watch
```

The watcher:

- Monitors C# sources, project/configuration files, and evaluated linked-source directories
- Excludes `obj/`, `bin/`, `node_modules/`, `.git/`, and its generated output directory
- Debounces changes (default 300ms, configurable with `--debounce`)
- Coalesces bursts of file changes into a single regeneration
- Never overlaps regenerations; changes that arrive during a run are queued for the next cycle
- Falls back to a full regeneration if the file watcher overflows
- Stops on `Ctrl+C`

### Build Integration

Pathfinder also ships an opt-in MSBuild companion package for build and `dotnet watch build` workflows.

1. Install Pathfinder as a local tool:

```bash
dotnet new tool-manifest
dotnet tool install InertiaNet.Pathfinder --prerelease
dotnet tool restore
```

2. Add the build package to your ASP.NET Core project:

```xml
<ItemGroup>
  <PackageReference Include="InertiaNet.Pathfinder.Build" Version="3.5.2-alpha.13" PrivateAssets="all" />
</ItemGroup>
```

3. Enable it in your project file:

```xml
<PropertyGroup>
  <PathfinderEnabled>true</PathfinderEnabled>
  <PathfinderOutputPath>$(MSBuildProjectDirectory)/ClientApp/pathfinder</PathfinderOutputPath>
</PropertyGroup>
```

Available MSBuild properties:

- `PathfinderEnabled`
- `PathfinderProjectPath`
- `PathfinderOutputPath`
- `PathfinderGenerateActions`
- `PathfinderGenerateRoutes`
- `PathfinderGenerateForms`
- `PathfinderGenerateModels`
- `PathfinderGenerateEnums`
- `PathfinderGenerateProps`
- `PathfinderDiscoverWolverine`
- `PathfinderFramework` (defaults to the first declared framework)
- `PathfinderContractFile` (optional explicit JSON output/input profiles)
- `PathfinderEndpointManifest` (optional exported registered-endpoint snapshot)
- `PathfinderStrict`
- `PathfinderSkipPatterns`
- `PathfinderClean`
- `PathfinderQuiet`
- `PathfinderToolCommand`
- `PathfinderBeforeTargets`

Once enabled, `dotnet build` will run Pathfinder before compile, and `dotnet watch build` will rerun it automatically as your C# files change.

The companion tracks source membership, evaluated linked/configuration inputs, generation options, and missing generated files. A successful-generation stamp allows unchanged modules to retain their timestamps without forcing repeated generation. Project paths and filter values are passed through environment variables; `PathfinderToolCommand` itself remains a trusted command supplied by the project. Generation uses the build's `Configuration`; use `PathfinderFramework` to choose a consistent contract for shared multi-target output.

---

## Skip Patterns

Exclude controllers or routes from generation using wildcard patterns:

```bash
# Skip health check and admin controllers
pathfinder --skip "*Health*;Admin*"
```

Pattern matching:

- `*Health*` — matches any controller containing "Health"
- `Admin*` — matches controllers starting with "Admin"
- `*Controller` — matches controllers ending with "Controller"
- `ExactName` — exact match (case-insensitive)

---

## Usage with Inertia.js

### React

```tsx
import { posts as Posts } from './pathfinder/actions'
import type { PostsIndexProps } from './pathfinder/types/Posts.Index'
import { router, useForm, Link } from '@inertiajs/react'

// Typed page component
export default function PostsIndex({ posts, totalCount }: PostsIndexProps) {
  const form = useForm({ title: '', body: '' })

  return (
    <div>
      <h1>Posts ({totalCount})</h1>

      {/* Navigate with Link */}
      {posts.map(post => (
        <Link key={post.id} href={Posts.show.url({ id: post.id })}>
          {post.title}
        </Link>
      ))}

      {/* Programmatic navigation */}
      <button onClick={() => router.visit(Posts.index())}>
        Refresh
      </button>

      {/* Form submission */}
      <form onSubmit={e => { e.preventDefault(); form.submit(Posts.store()) }}>
        <input value={form.data.title} onChange={e => form.setData('title', e.target.value)} />
        <button type="submit">Create Post</button>
      </form>
    </div>
  )
}
```

### Vue

```vue
<script setup lang="ts">
import { posts as Posts } from './pathfinder/actions'
import type { PostsIndexProps } from './pathfinder/types/Posts.Index'
import { router, useForm, Link } from '@inertiajs/vue3'

defineProps<PostsIndexProps>()

const form = useForm({ title: '', body: '' })

function submit() {
  form.submit(Posts.store())
}
</script>

<template>
  <div>
    <Link v-for="post in posts" :key="post.id" :href="Posts.show.url({ id: post.id })">
      {{ post.title }}
    </Link>

    <form @submit.prevent="submit">
      <input v-model="form.title" />
      <button type="submit">Create Post</button>
    </form>
  </div>
</template>
```

---

## Importing

```typescript
// Import a specific controller's actions
import Posts from './pathfinder/actions/posts'
// Alternative: import { posts as Posts } from './pathfinder/actions'

// Import all actions
import * as actions from './pathfinder/actions'
actions.posts.index()

// Import named routes
import { posts as Posts } from './pathfinder/routes'

// Import page prop types
import type { DashboardProps } from './pathfinder/types/Dashboard'

// Import models
import type { Post } from './pathfinder/models/Post'

// Import enums
import { OrderStatus } from './pathfinder/enums/OrderStatus'

// Import runtime utilities
import { setUrlDefaults, addUrlDefault, validateParameters, queryParams } from './pathfinder'
```

---

## Runtime Types and Utilities

The generated `index.ts` exports the following types and utilities:

```typescript
// Return type of action helpers
type RouteDefinition<T = string> = { url: string; method: T }

// Static route metadata on .definition
type RouteDefinitionInfo<T extends string[] = string[]> = { methods: T; url: string }

// Options parameter for query strings
type RouteQueryOptions = {
    query?: QueryParams
    mergeQuery?: QueryParams
    arrayFormat?: 'repeat' | 'brackets' | 'indices'
    objectFormat?: 'dots' | 'brackets'
}

// Return type of .form() helpers (data contains _method for PUT/PATCH/DELETE spoofing)
type FormDefinition = { action: string; method: "get" | "post"; data?: Record<string, string> }

// Query string builder (used internally, also exported for custom use)
// Default nested object syntax: { filter: { status: 'active' } } → filter.status=active
function queryParams(options?: RouteQueryOptions): string

// Set global URL defaults for route parameter substitution
function setUrlDefaults(defaults: Record<string, unknown> | (() => Record<string, unknown>)): void

// Add a single URL default
function addUrlDefault(key: string, value: string | number | boolean): void

// Apply URL defaults to args (used internally, also exported for custom use)
function applyUrlDefaults(args: Record<string, unknown>): Record<string, unknown>

// Validate required parameters and optional trailing omission order (used internally)
function validateParameters(routeName: string, url: string, required: string[], args: Record<string, unknown>, optional?: string[]): void
```

---

## How It Works

1. **Evaluate** — Reads the project's evaluated `Compile` inputs and framework/configuration symbols, or scans a source directory
2. **Parse and compile for analysis** — Parses inputs and uses `ProjectCompilationLoader` to build a Roslyn compilation with available project and metadata references, without starting the app
3. **Discover routes** — Scans MVC controllers, Minimal APIs, and optionally Wolverine endpoints, then applies skip patterns
4. **Discover contracts** — Uses `SemanticContractDiscoverer` with explicit or default input/output serialization profiles to produce routes, enums, models, page props, and shared props
5. **Validate** — Optionally compares an exported endpoint manifest; errors or strict-mode diagnostics stop before generation
6. **Generate** — Stages TypeScript files for the selected actions, routes, enums, types, and models, including runtime utilities and barrel exports
7. **Synchronize** — Checks freshness or synchronizes staged output with the generated directory

Pathfinder does not start your application. Project evaluation uses `dotnet msbuild` with Pathfinder disabled to prevent recursive generation. Dynamic runtime registrations and custom serializer behavior that static discovery cannot resolve still require explicit contracts or diagnostics.

---

## Interface

The supported entry point is the `pathfinder` CLI described above. Its implementation types are internal to the tool assembly.

- `PathfinderConfig.FromArgs` parses CLI switches and internal `--msbuild` context from `PATHFINDER_BUILD_*` environment values.
- `ProjectSourceLoader.LoadAsync` returns `ProjectSources` with evaluated files, references, imports, parse settings, and watched inputs.
- `PathfinderWatchRunner.RunAsync` watches source/project inputs and invokes regeneration after debouncing changes.
- [Analysis](./Analysis/README.md) supplies normalized routes, serialization contracts, and diagnostics.
- [Generation](./Generation/README.md) stages TypeScript artifacts and synchronizes output.

## Invariants

- Project directories containing several `.csproj` files require an explicit project path. Requested frameworks must be declared by that project; `--framework` requires a project.
- Analysis does not start application code. Project evaluation invokes `dotnet msbuild` with `PathfinderEnabled=false` to prevent recursive generation.
- Boolean switches require `true` or `false`; debounce must be between 1 and 60000 milliseconds. `--check` and `--watch` cannot be combined.
- Error diagnostics, and all discovery diagnostics under strict mode, stop before generation. Quiet mode leaves diagnostics on stderr.
- Exit codes are `0` for success/current output or cancellation, `1` for analysis errors, strict diagnostics, or stale checked output, and `2` for handled argument, evaluation, serialization, or output failures.
- Watch mode ignores the generated output, `bin`, `obj`, `node_modules`, and `.git`; it includes external input directories and refreshes its watchers after regeneration.

## Side Effects

The tool reads project/source/configuration files, serialization profiles, endpoint snapshots, and framework/package metadata. It starts `dotnet msbuild` for evaluation and preprocessing, including a temporary preprocessing file. Generation writes TypeScript and bookkeeping files; `--check` preserves the output directory. Progress goes to stdout and diagnostics to stderr. Watch mode installs filesystem watchers and a change channel until cancellation; console cancellation handlers and watchers are disposed when the run ends.

## Verification

Working directory: the repository root (`dotnet-inertia-adapter/`). The .NET test project targets .NET 10. Install the TypeScript test package's dependencies before its npm command.

```sh
dotnet test tests/InertiaNet.Pathfinder.Tests/InertiaNet.Pathfinder.Tests.csproj
npm --prefix tests/InertiaNet.Pathfinder.TypeScript test
```

The TypeScript suite generates fixtures, typechecks, builds, and runs Node runtime/protocol tests.

---

## License

MIT
