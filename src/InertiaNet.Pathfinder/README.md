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

**Targets:** .NET 9

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
dotnet tool install --global InertiaNet.Pathfinder
```

### As a local tool

```bash
dotnet tool install InertiaNet.Pathfinder
```

---

## Quick Start

### 1. Run Pathfinder

```bash
# Scan the current directory and output to ./pathfinder
pathfinder

# Scan a specific project
pathfinder -p ./src/MyApp -o ./src/frontend/pathfinder
```

### 2. Import and use in your frontend

```typescript
import { Posts } from './pathfinder/actions'

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
// → { action: "/posts/42", method: "post", data: { _method: "put" } }
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
| `--actions <bool>` | | `true` | Generate action files |
| `--routes <bool>` | | `true` | Generate named route files |
| `--forms <bool>` | | `true` | Generate `.form()` helpers on actions and routes |
| `--skip <patterns>` | | | Semicolon-separated skip patterns (e.g. `*Health*;Admin*`) |
| `--clean <bool>` | | `true` | Clean output directory before generation |
| `--no-clean` | | | Shorthand for `--clean false` |
| `--watch` | `-w` | `false` | Watch for file changes and regenerate |
| `--debounce <ms>` | | `300` | Debounce delay for watch mode (milliseconds) |
| `--quiet` | `-q` | `false` | Suppress console output |

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
- Minimal APIs using `MapGet` / `MapPost` / `MapPut` / `MapPatch` / `MapDelete`
- Minimal API route templates resolved from string literals, constants, and simple interpolated/concatenated static strings
- Minimal API groups assigned to variables via `var posts = app.MapGroup("/posts")`
- Minimal API inline `MapGroup(...)` chains when every prefix segment can be resolved statically
- Minimal API lambda handlers (`() => ...`, `(id) => ...`, `(id, [FromBody] dto) => ...`)
- Same-file Minimal API method-group handlers (`MapGet(..., GetPost)`) when the target method can be resolved in the same C# file

### Explicitly Unsupported For Now

- Minimal API route templates or group prefixes that depend on runtime values or other non-resolvable expressions
- Cross-file or otherwise non-resolvable Minimal API method-group handlers for parameter/body discovery

Unsupported patterns are skipped with a warning instead of generating partial or misleading output.

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

Generates `pathfinder/actions/posts.ts`:

```typescript
import { type RouteDefinition, type RouteDefinitionInfo, type RouteQueryOptions, type FormDefinition, queryParams, applyUrlDefaults, validateParameters } from '../index'

/** @see src/Controllers/PostsController.cs:10 */
export const index = (options?: RouteQueryOptions): RouteDefinition<"get"> => ({
    url: index.url(options), method: "get",
})
index.definition = { methods: ["get","head"], url: "/api/posts" } satisfies RouteDefinitionInfo<["get","head"]>
index.url = (options?: RouteQueryOptions) => index.definition.url + queryParams(options)
index.get = (options?: RouteQueryOptions) => ({ url: index.url(options), method: "get" as const })
index.head = (options?: RouteQueryOptions) => ({ url: index.url(options), method: "head" as const })
index.form = (options?: RouteQueryOptions): FormDefinition => ({
    action: index.url(options), method: "get",
})

/** @see src/Controllers/PostsController.cs:18 */
export const show = (
    args: { id: number } | [id: number] | number,
    options?: RouteQueryOptions
): RouteDefinition<"get"> => ({ url: show.url(args, options), method: "get" })
show.definition = { methods: ["get","head"], url: "/api/posts/{id}" } satisfies RouteDefinitionInfo<["get","head"]>
show.url = (args: { id: number } | [id: number] | number, options?: RouteQueryOptions) => {
    if (typeof args === 'string' || typeof args === 'number') args = { id: args }
    if (Array.isArray(args)) args = { id: args[0] }
    args = applyUrlDefaults(args ?? {}) as typeof args
    validateParameters("show", show.definition.url, ["id"], args as Record<string, unknown>)
    return show.definition.url.replace("{id}", String(args.id ?? '')).replace(/\/+$/, '') + queryParams(options)
}

// update uses method spoofing — HTML forms only support GET and POST
update.form = (args: { id: number } | [id: number] | number, options?: RouteQueryOptions): FormDefinition => ({
    action: update.url(args, options), method: "post",
    data: { _method: "put" },
})

const Posts = { index, show, store, update, destroy }
export default Posts
```

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
// → /api/posts?tags[]=news&tags[]=tech

// Nested object query params
index({ query: { filter: { status: 'active', sort: 'date' } } })
// → /api/posts?filter[status]=active&filter[sort]=date
```

---

## Form Helpers

The `.form()` method returns `{ action, method, data? }` for HTML form attributes. For HTTP methods not supported by HTML forms (PUT, PATCH, DELETE), it automatically uses method spoofing — sending as POST with `_method` in the `data` field:

```typescript
import { Posts } from './pathfinder/actions'

// GET/POST routes — no spoofing needed
Posts.index.form()
// → { action: "/api/posts", method: "get" }

Posts.store.form()
// → { action: "/api/posts", method: "post" }

// PUT/PATCH/DELETE routes — automatic method spoofing
Posts.update.form({ id: 42 })
// → { action: "/api/posts/42", method: "post", data: { _method: "put" } }

Posts.destroy.form({ id: 42 })
// → { action: "/api/posts/42", method: "post", data: { _method: "delete" } }
```

The `data._method` value should be included as a hidden form field or in the request body. ASP.NET Core's `HttpMethodOverrideMiddleware` reads `_method` from the request body or `X-HTTP-Method-Override` header.

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
import { Posts } from './pathfinder/routes'

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
inertia.Render<DashboardProps>("Dashboard");
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
inertia.Render("Posts/Index", new
{
    Posts = posts,
    TotalCount = count,
});
```

Generates `pathfinder/types/Posts.Index.ts`:

```typescript
export interface PostsIndexProps {
    posts: unknown
    totalCount: number
}
```

> Anonymous objects provide best-effort type inference. For the most accurate types, use typed prop classes with `Render<T>()`.

### Supported type mappings for props

| C# Type | TypeScript Type |
|---|---|
| `string`, `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `Uri` | `string` |
| `int`, `long`, `short`, `byte`, `float`, `double`, `decimal` | `number` |
| `bool` | `boolean` |
| `object` | `unknown` |
| `T?` / `Nullable<T>` | `T \| null` |
| `List<T>`, `IEnumerable<T>`, `T[]` | `T[]` |
| `Dictionary<K, V>` | `Record<K, V>` |
| `Func<T>`, `Lazy<T>`, `Task<T>` | Unwrapped to `T` |
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

Models are only generated for types actually referenced by Inertia page props — not every class in your project.

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

export type OrderStatus = (typeof OrderStatus)[keyof typeof OrderStatus]
// → 0 | 1 | 2
```

String-valued enums generate quoted values:

```csharp
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

export type Color = (typeof Color)[keyof typeof Color]
// → "Red" | "Green" | "Blue"
```

Discovered enums are registered in the type mapper, so route parameters and model properties typed with an enum will use the enum type name instead of `string | number`.

---

## Route Constraints

Route template constraints are parsed and mapped to TypeScript types:

| Constraint | TypeScript Type |
|---|---|
| `{id:int}`, `{id:long}`, `{id:float}`, `{id:double}`, `{id:decimal}` | `number` |
| `{slug:alpha}`, `{name:minlength(3)}`, `{code:maxlength(10)}` | `string` |
| `{active:bool}` | `boolean` |
| `{key:guid}` | `string` |
| `{id:min(1)}`, `{id:max(100)}`, `{id:range(1,100)}` | `number` |
| `{id?}` (optional) | Parameter marked optional |

Constraints are stripped from the generated URL template — `{id:int}` becomes `{id}` in the output.

---

## Watch Mode

Use `--watch` to automatically regenerate when C# files change:

```bash
pathfinder -p ./src/MyApp -o ./src/frontend/pathfinder --watch
```

The watcher:
- Monitors all `.cs` files recursively (excludes `obj/` and `bin/`)
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
dotnet tool install InertiaNet.Pathfinder
dotnet tool restore
```

2. Add the build package to your ASP.NET Core project:

```xml
<ItemGroup>
  <PackageReference Include="InertiaNet.Pathfinder.Build" Version="0.1.0-alpha.1" PrivateAssets="all" />
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
- `PathfinderSkipPatterns`
- `PathfinderClean`
- `PathfinderQuiet`
- `PathfinderToolCommand`
- `PathfinderBeforeTargets`

Once enabled, `dotnet build` will run Pathfinder before compile, and `dotnet watch build` will rerun it automatically as your C# files change.

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
import { Posts } from './pathfinder/actions'
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
import { Posts } from './pathfinder/actions'
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
import { Posts } from './pathfinder/actions'
import Posts from './pathfinder/actions/posts'

// Import all actions
import * as actions from './pathfinder/actions'
actions.Posts.index()

// Import named routes
import { Posts } from './pathfinder/routes'

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
type RouteQueryOptions = { query?: QueryParams; mergeQuery?: QueryParams }

// Return type of .form() helpers (data contains _method for PUT/PATCH/DELETE spoofing)
type FormDefinition = { action: string; method: "get" | "post"; data?: Record<string, string> }

// Query string builder (used internally, also exported for custom use)
// Supports nested objects with bracket notation: { filter: { status: 'active' } } → filter[status]=active
function queryParams(options?: RouteQueryOptions): string

// Set global URL defaults for route parameter substitution
function setUrlDefaults(defaults: Record<string, string> | (() => Record<string, string>)): void

// Add a single URL default
function addUrlDefault(key: string, value: string): void

// Apply URL defaults to args (used internally, also exported for custom use)
function applyUrlDefaults(args: Record<string, unknown>): Record<string, unknown>

// Validate required parameters and optional trailing omission order (used internally)
function validateParameters(routeName: string, url: string, required: string[], args: Record<string, unknown>, optional?: string[]): void
```

---

## How It Works

1. **Scan** — Finds all `.cs` files in the project (excluding `obj/` and `bin/`)
2. **Parse** — Parses each file into a Roslyn syntax tree (static analysis, no compilation needed)
3. **Discover routes** — Three independent discoverers scan for MVC controllers, Minimal APIs, and Wolverine endpoints
4. **Discover enums** — Finds all enum declarations
5. **Discover props** — Finds `Render()` / `Inertia()` calls and extracts prop shapes
6. **Discover models** — Finds classes referenced by page props and extracts their properties
7. **Generate** — Writes TypeScript files for actions, routes, enums, types, and models
8. **Barrel** — Generates `index.ts` re-export files for clean imports

Since Pathfinder uses Roslyn syntax tree analysis (not compilation), it works without building your project and has no runtime dependencies. It only needs your source files.

---

## License

MIT
