## README.md

- **[P2] Minimal API history-encryption example references a missing type.** The [History Encryption example](./README.md#history-encryption) (line 552) calls `AddEndpointFilter<EncryptHistoryEndpointFilter>()`, but the repository declares no such filter. [EncryptHistoryMiddleware.cs](./src/InertiaNet/Middleware/EncryptHistoryMiddleware.cs) implements only `EncryptHistoryMiddleware` and the MVC `EncryptHistoryAttribute`; its XML example repeats the unresolved type. The documented Minimal API example cannot compile as supplied. Rewrite the example around the existing scoped `IInertiaService.EncryptHistory()` API, or implement the filter in a separate code change.
- **[P2] Exact large-integer flash transport does not survive every redirect.** The [Large integers section](./README.md#large-integers) (line 343) says the `String` and `BigInt` policies cover flash, while [Flash Data](./README.md#flash-data) promises redirect persistence. However, [InertiaMiddleware.cs](./src/InertiaNet/Middleware/InertiaMiddleware.cs) persists flash with its separate `FlashJsonOptions` (lines 263 and 305–308), without the configured integer converters, and restores numeric values with `TryGetInt64` followed by `GetDouble` (line 321). A top-level flashed `ulong.MaxValue` therefore becomes a rounded `double` after a redirect; later page serialization cannot restore its exact digits or apply the integer transport to that value. Direct-render flash coverage does not establish this redirect path. Qualify the guarantee or preserve exact integer values through TempData serialization and restoration.

## samples/README.md

No drift detected.

## samples/Playground/README.md

No drift detected.

## samples/Playground/ClientApp/README.md

No drift detected.

## samples/Playground/ClientApp/src/README.md

No drift detected.

## samples/Playground/ClientApp/src/react/README.md

No drift detected.

## samples/Playground/Controllers/README.md

No drift detected.

## scripts/README.md

No drift detected.

## src/README.md

No drift detected.

## src/InertiaNet/README.md

No drift detected.

## src/InertiaNet.Analyzers/README.md

No drift detected.

## src/InertiaNet.Pathfinder/README.md

- **[P2] Route-constraint table understates the generated `long` argument type.** The [Route Constraints table](./src/InertiaNet.Pathfinder/README.md#route-constraints) (line 661) groups `{id:long}` under `number`. In [SemanticContractDiscoverer.cs](./src/InertiaNet.Pathfinder/Analysis/SemanticContractDiscoverer.cs), `EnrichRoute` maps bound route parameters using native query/binding rules (lines 514–521), and `Map` emits `number | string` for large integer route/query/form values, adding `bigint` under the BigInt output profile (lines 266–267). The declaration emitted by [RouteFunctionWriter.cs](./src/InertiaNet.Pathfinder/Generation/RouteFunctionWriter.cs) uses this enriched type. Split the `long` entry and document its accepted union and BigInt variant.
- **[P3] Runtime type reference shows an outdated `FormDefinition` declaration.** The [Runtime Types and Utilities example](./src/InertiaNet.Pathfinder/README.md#runtime-types-and-utilities) (line 910) declares a nongeneric `FormDefinition` with `method: "get" | "post"`. [RuntimeFileWriter.cs](./src/InertiaNet.Pathfinder/Generation/RuntimeFileWriter.cs) (line 16) actually exports `FormDefinition<TMethod extends string = string>` with `method: TMethod`; [RouteFunctionWriter.cs](./src/InertiaNet.Pathfinder/Generation/RouteFunctionWriter.cs) specializes it for each generated form method. Update the reference declaration so consumers can discover and use its generic method parameter.

## src/InertiaNet.Pathfinder/Analysis/README.md

No drift detected.

## src/InertiaNet.Pathfinder/Generation/README.md

No drift detected.

## src/InertiaNet.Templates/README.md

No drift detected.

## src/InertiaNet.Templates/templates/README.md

No drift detected.

## src/InertiaNet.Templates/templates/react-starter/README.md

No drift detected.

## src/InertiaNet.Templates/templates/react-starter/ClientApp/README.md

No drift detected.

## src/InertiaNet.Templates/templates/react-starter/ClientApp/src/README.md

No drift detected.

## src/InertiaNet.Templates/templates/react-starter/ClientApp/src/pages/README.md

No drift detected.

## src/InertiaNet.Templates/templates/vue-starter/README.md

No drift detected.

## src/InertiaNet.Templates/templates/vue-starter/ClientApp/README.md

No drift detected.

## src/InertiaNet.Templates/templates/vue-starter/ClientApp/src/README.md

No drift detected.

## src/InertiaNet/Context/README.md

No drift detected.

## src/InertiaNet/Core/README.md

No drift detected.

## src/InertiaNet/Diagnostics/README.md

No drift detected.

## src/InertiaNet/Extensions/README.md

No drift detected.

## src/InertiaNet/Interfaces/README.md

No drift detected.

## src/InertiaNet/Middleware/README.md

No drift detected.

## src/InertiaNet/Props/README.md

No drift detected.

## src/InertiaNet/Resolution/README.md

No drift detected.

## src/InertiaNet/Ssr/README.md

No drift detected.

## src/InertiaNet/Support/README.md

No drift detected.

## src/InertiaNet/TagHelpers/README.md

No drift detected.

## src/InertiaNet/Testing/README.md

No drift detected.

## src/InertiaNet/Vite/README.md

No drift detected.
