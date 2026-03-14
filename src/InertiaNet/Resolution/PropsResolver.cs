using InertiaNet.Context;
using InertiaNet.Interfaces;
using InertiaNet.Props;
using InertiaNet.Support;
using Microsoft.AspNetCore.Http;

namespace InertiaNet.Resolution;

/// <summary>
/// Resolves the full set of Inertia props for a request, applying partial reload
/// filtering, initial-load exclusions, prop type unwrapping, and metadata collection.
/// This is a faithful port of Laravel's PropsResolver (src/PropsResolver.php).
/// </summary>
internal sealed class PropsResolver
{
    private readonly HttpContext _context;
    private readonly string _component;
    private readonly IServiceProvider _services;

    // Parsed from request headers
    private readonly bool _isPartial;
    private readonly bool _isInertia;
    private readonly string[] _only;
    private readonly string[] _except;
    private readonly string[] _resetProps;
    private readonly string[] _loadedOnceProps;

    // Collected metadata (populated during resolution)
    private readonly List<string> _sharedPropKeys = [];
    private readonly List<string> _mergeProps = [];
    private readonly List<string> _prependProps = [];
    private readonly List<string> _deepMergeProps = [];
    private readonly Dictionary<string, object?> _matchPropsOn = [];
    private readonly Dictionary<string, List<string>> _deferredProps = [];
    private readonly Dictionary<string, object?> _scrollProps = [];
    private readonly Dictionary<string, object?> _onceProps = [];

    public PropsResolver(HttpContext context, string component, IServiceProvider services)
    {
        _context = context;
        _component = component;
        _services = services;

        var request = context.Request;
        _isInertia = request.Headers.ContainsKey(HeaderNames.Inertia);

        var partialComponent = request.Headers[HeaderNames.PartialComponent].ToString();
        _isPartial = _isInertia && partialComponent == component;

        _only = ParseHeader(request.Headers[HeaderNames.PartialOnly]);
        _except = ParseHeader(request.Headers[HeaderNames.PartialExcept]);
        _resetProps = ParseHeader(request.Headers[HeaderNames.Reset]);
        _loadedOnceProps = ParseHeader(request.Headers[HeaderNames.ExceptOnceProps]);
    }

    /// <summary>
    /// Resolves shared and page props, returning the final props dictionary
    /// and all collected metadata.
    /// </summary>
    public async Task<(Dictionary<string, object?> Props, ResolvedMetadata Metadata)> ResolveAsync(
        Dictionary<string, object?> sharedProps,
        Dictionary<string, object?> pageProps,
        CancellationToken ct = default)
    {
        // 1. Resolve IProvidesInertiaProperties in shared props, collect shared keys
        var resolvedShared = await ResolveProvidersAsync(sharedProps, isShared: true, ct);

        // 2. Resolve IProvidesInertiaProperties in page props
        var resolvedPage = await ResolveProvidersAsync(pageProps, isShared: false, ct);

        // 3. Merge: shared props first, page props override
        var merged = new Dictionary<string, object?>(resolvedShared);
        foreach (var kvp in resolvedPage)
            merged[kvp.Key] = kvp.Value;

        // 4. Unpack top-level dot-notation keys
        var unpacked = UnpackDotKeys(merged);

        // 5. Recursively resolve all prop values
        var resolved = await ResolvePropsAsync(unpacked, parentPath: null, ct);

        var metadata = new ResolvedMetadata(
            SharedPropKeys: _sharedPropKeys.Count > 0 ? _sharedPropKeys : null,
            MergeProps: _mergeProps.Count > 0 ? _mergeProps : null,
            PrependProps: _prependProps.Count > 0 ? _prependProps : null,
            DeepMergeProps: _deepMergeProps.Count > 0 ? _deepMergeProps : null,
            MatchPropsOn: _matchPropsOn.Count > 0 ? _matchPropsOn : null,
            DeferredProps: _deferredProps.Count > 0 ? BuildDeferredDict() : null,
            ScrollProps: _scrollProps.Count > 0 ? _scrollProps : null,
            OnceProps: _onceProps.Count > 0 ? _onceProps : null
        );

        return (resolved, metadata);
    }

    // ─── Shared/Page Props: Resolve IProvidesInertiaProperties ──────────────

    private async Task<Dictionary<string, object?>> ResolveProvidersAsync(
        Dictionary<string, object?> props, bool isShared, CancellationToken ct)
    {
        var result = new Dictionary<string, object?>();
        var renderCtx = new RenderContext(_component, _context);

        foreach (var kvp in props)
        {
            if (kvp.Value is IProvidesInertiaProperties provider)
            {
                // Expand the provider into multiple keys
                foreach (var (k, v) in provider.ToInertiaProperties(renderCtx))
                {
                    result[k] = v;
                    if (isShared) _sharedPropKeys.Add(k);
                }
            }
            else
            {
                result[kvp.Key] = kvp.Value;
                if (isShared) _sharedPropKeys.Add(kvp.Key);
            }
        }

        return result;
    }

    // ─── Dot-notation key unpacking ─────────────────────────────────────────

    /// <summary>
    /// Expands top-level dot-notation keys into nested dictionaries.
    /// Only top-level keys are unpacked; dots in nested arrays remain literal.
    /// </summary>
    private static Dictionary<string, object?> UnpackDotKeys(Dictionary<string, object?> props)
    {
        var result = new Dictionary<string, object?>();

        foreach (var kvp in props)
        {
            if (kvp.Key.Contains('.'))
            {
                var parts = kvp.Key.Split('.', 2);
                var outerKey = parts[0];
                var innerKey = parts[1];

                if (!result.TryGetValue(outerKey, out var existing) || existing is not Dictionary<string, object?> nested)
                {
                    nested = [];
                    result[outerKey] = nested;
                }
                nested[innerKey] = kvp.Value;
            }
            else
            {
                result[kvp.Key] = kvp.Value;
            }
        }

        return result;
    }

    // ─── Recursive prop resolution ───────────────────────────────────────────

    private async Task<Dictionary<string, object?>> ResolvePropsAsync(
        Dictionary<string, object?> props, string? parentPath, CancellationToken ct)
    {
        var result = new Dictionary<string, object?>();
        var propCtx = new PropertyContext(string.Empty, props, _context);

        foreach (var kvp in props)
        {
            var key = kvp.Key;
            var value = kvp.Value;
            var path = parentPath is null ? key : $"{parentPath}.{key}";

            // Step 1: Partial reload filtering
            if (_isPartial && !ShouldIncludeInPartialResponse(value, path))
                continue;

            // Step 2: Initial load exclusions (IIgnoreFirstLoad, deferred, already-loaded once-props)
            if (!_isPartial && ShouldExcludeFromInitialResponse(value, path))
                continue;

            // Step 3: Resolve the value
            var resolved = await ResolveValueAsync(value, key, props, ct);

            // Step 4: Double-unwrap — if a delegate returned a prop type, resolve it again
            resolved = await UnwrapPropTypeAsync(resolved, key, path, props, ct);

            // Step 5: Collect merge/once/scroll metadata from the original prop wrapper
            CollectMetadata(value, path);

            // Step 6: If resolved value is a nested dictionary, recurse into it
            if (resolved is Dictionary<string, object?> nested)
            {
                resolved = await ResolvePropsAsync(nested, path, ct);
            }

            result[key] = resolved;
        }

        return result;
    }

    // ─── Partial reload filtering ────────────────────────────────────────────

    /// <summary>
    /// Returns true if this prop should be included in a partial reload response.
    /// AlwaysProp bypasses all filtering. Others are checked against only/except lists.
    /// </summary>
    private bool ShouldIncludeInPartialResponse(object? value, string path)
    {
        // AlwaysProp is always included, even in partials
        if (value is AlwaysProp)
            return true;

        // If we have an "only" list, this path must match it
        if (_only.Length > 0)
            return _only.Any(p => PathMatches(path, p));

        // If we have an "except" list, this path must NOT match it
        if (_except.Length > 0)
            return !_except.Any(p => PathMatches(path, p));

        return true;
    }

    /// <summary>
    /// Bidirectional prefix matching for partial reload paths.
    /// "auth" matches "auth", "auth.user", "auth.user.can".
    /// "auth.user" also matches parent "auth" so the resolver can traverse into it.
    /// </summary>
    private static bool PathMatches(string path, string filterPath)
    {
        // Exact match
        if (path == filterPath) return true;
        // path is a child of filterPath (e.g. path="auth.user" filterPath="auth")
        if (path.StartsWith(filterPath + ".", StringComparison.Ordinal)) return true;
        // path is a parent of filterPath — include the parent so we can traverse (e.g. path="auth" filterPath="auth.user")
        if (filterPath.StartsWith(path + ".", StringComparison.Ordinal)) return true;
        return false;
    }

    // ─── Initial load exclusions ─────────────────────────────────────────────

    /// <summary>
    /// Returns true if this prop should be excluded from the initial (non-partial) response.
    /// </summary>
    private bool ShouldExcludeFromInitialResponse(object? value, string path)
    {
        // IIgnoreFirstLoad covers OptionalProp and DeferProp
        if (value is IIgnoreFirstLoad)
        {
            // Collect deferred props metadata
            if (value is IDeferrable deferrable && deferrable.ShouldDefer())
            {
                var group = deferrable.Group();
                if (!_deferredProps.TryGetValue(group, out var groupList))
                {
                    groupList = [];
                    _deferredProps[group] = groupList;
                }
                groupList.Add(path);
            }
            return true;
        }

        // ScrollProp with defer enabled
        if (value is IDeferrable scrollDeferred && scrollDeferred.ShouldDefer())
        {
            var group = scrollDeferred.Group();
            if (!_deferredProps.TryGetValue(group, out var groupList))
            {
                groupList = [];
                _deferredProps[group] = groupList;
            }
            groupList.Add(path);
            return true;
        }

        // Once-props already loaded by the client (sent via X-Inertia-Except-Once-Props)
        if (value is IOnceable onceable && onceable.ShouldResolveOnce())
        {
            var onceKey = onceable.GetKey() ?? path;
            if (_loadedOnceProps.Contains(onceKey) && !onceable.ShouldBeRefreshed())
            {
                // Include both 'prop' (the actual prop path) and 'expiresAt' per the v3 protocol
                _onceProps[onceKey] = new { prop = path, expiresAt = onceable.ExpiresAt() };
                return true;
            }
        }

        return false;
    }

    // ─── Value resolution ─────────────────────────────────────────────────────

    private async Task<object?> ResolveValueAsync(
        object? value, string key, Dictionary<string, object?> siblingProps, CancellationToken ct)
    {
        return value switch
        {
            // Delegate-based prop types
            AlwaysProp always       => await always.ResolveAsync(_services, ct),
            OptionalProp optional   => await optional.ResolveAsync(_services, ct),
            DeferProp defer         => await defer.ResolveAsync(_services, ct),
            MergeProp merge         => await merge.ResolveAsync(_services, ct),
            OnceProp once           => await once.ResolveAsync(_services, ct),
            ScrollProp scroll       => await ResolveScrollPropAsync(scroll, ct),

            // Custom prop interface
            IProvidesInertiaProperty provider =>
                provider.ToInertiaProperty(new PropertyContext(key, siblingProps, _context)),

            // Plain async delegate
            Func<IServiceProvider, CancellationToken, Task<object?>> func =>
                await func(_services, ct),

            // Plain synchronous delegate
            Func<object?> sync => sync(),

            // Everything else: pass through as-is
            _ => value
        };
    }

    private async Task<object?> ResolveScrollPropAsync(ScrollProp scroll, CancellationToken ct)
    {
        scroll.ConfigureMergeIntent(_context);
        var resolved = await scroll.ResolveAsync(_services, ct);
        return resolved;
    }

    /// <summary>
    /// If the resolved value is itself a prop type (e.g. a delegate returned a DeferProp),
    /// resolve it one more time. Matches PHP's "post-resolution prop type unwrap" behaviour.
    /// </summary>
    private async Task<object?> UnwrapPropTypeAsync(
        object? value, string key, string path, Dictionary<string, object?> siblingProps, CancellationToken ct)
    {
        // Only unwrap recognized prop wrapper types
        if (value is AlwaysProp or OptionalProp or DeferProp or MergeProp or OnceProp or ScrollProp
            or Func<IServiceProvider, CancellationToken, Task<object?>> or Func<object?>)
        {
            return await ResolveValueAsync(value, key, siblingProps, ct);
        }
        return value;
    }

    // ─── Metadata collection ──────────────────────────────────────────────────

    private void CollectMetadata(object? propWrapper, string path)
    {
        if (propWrapper is IMergeable mergeable && mergeable.ShouldMerge())
        {
            if (mergeable.ShouldDeepMerge() && !_deepMergeProps.Contains(path))
                _deepMergeProps.Add(path);

            if (mergeable.AppendsAtRoot() && !_mergeProps.Contains(path))
                _mergeProps.Add(path);

            if (mergeable.PrependsAtRoot() && !_prependProps.Contains(path))
                _prependProps.Add(path);

            foreach (var appendPath in mergeable.AppendsAtPaths())
            {
                var full = $"{path}.{appendPath}";
                if (!_mergeProps.Contains(full)) _mergeProps.Add(full);
            }

            foreach (var prependPath in mergeable.PrependsAtPaths())
            {
                var full = $"{path}.{prependPath}";
                if (!_prependProps.Contains(full)) _prependProps.Add(full);
            }

            var matchesOn = mergeable.MatchesOn();
            if (matchesOn.Count > 0)
                _matchPropsOn[path] = matchesOn;
        }

        if (propWrapper is IOnceable onceable && onceable.ShouldResolveOnce())
        {
            var onceKey = onceable.GetKey() ?? path;
            // Always include both 'prop' (the actual prop path) and 'expiresAt' per the v3 protocol.
            // 'prop' may differ from 'onceKey' when a custom key is set via .As("key").
            _onceProps[onceKey] = new { prop = path, expiresAt = onceable.ExpiresAt() };
        }

        if (propWrapper is ScrollProp scroll)
        {
            // Scroll metadata is collected after ConfigureMergeIntent is called during resolve
            _scrollProps[path] = null; // populated async below; placeholder
        }
    }

    /// <summary>
    /// After all props are resolved, back-fill actual scroll metadata from resolved ScrollProps.
    /// Called as part of the post-resolution pass.
    /// </summary>
    public async Task CollectScrollMetadataAsync(
        Dictionary<string, object?> originalProps, CancellationToken ct)
    {
        foreach (var key in _scrollProps.Keys.ToList())
        {
            if (originalProps.TryGetValue(key, out var prop) && prop is ScrollProp scroll)
            {
                var meta = await scroll.ResolveMetadataAsync(_services, ct);
                _scrollProps[key] = meta is ScrollMetadata sm ? sm.ToInertiaFormat() : null;
            }
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string[] ParseHeader(Microsoft.Extensions.Primitives.StringValues header)
    {
        var raw = header.ToString();
        return string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private IReadOnlyDictionary<string, IReadOnlyList<string>> BuildDeferredDict()
    {
        return _deferredProps.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)kvp.Value.AsReadOnly());
    }
}

/// <summary>
/// All metadata collected by <see cref="PropsResolver"/> during a single resolution pass.
/// </summary>
internal sealed record ResolvedMetadata(
    IReadOnlyList<string>? SharedPropKeys,
    IReadOnlyList<string>? MergeProps,
    IReadOnlyList<string>? PrependProps,
    IReadOnlyList<string>? DeepMergeProps,
    IReadOnlyDictionary<string, object?>? MatchPropsOn,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? DeferredProps,
    IReadOnlyDictionary<string, object?>? ScrollProps,
    IReadOnlyDictionary<string, object?>? OnceProps
);
