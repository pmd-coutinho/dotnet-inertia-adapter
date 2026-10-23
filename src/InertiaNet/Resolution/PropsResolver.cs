using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using InertiaNet.Core;
using InertiaNet.Context;
using InertiaNet.Interfaces;
using InertiaNet.Props;
using InertiaNet.Support;
using InertiaNet.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InertiaNet.Resolution;

/// <summary>Resolves props and their protocol metadata in the same traversal.</summary>
internal sealed class PropsResolver
{
    private readonly HttpContext _context;
    private readonly string _component;
    private readonly IServiceProvider _services;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly PropDiagnosticsTrace? _diagnostics;
    private readonly HashSet<object> _ancestors = new(ReferenceEqualityComparer.Instance);
    private readonly bool _isPartial;
    private readonly bool _isInertia;
    private readonly string[] _only;
    private readonly string[] _except;
    private readonly string[] _resetProps;
    private readonly string[] _loadedOnceProps;
    private readonly List<string> _sharedPropKeys = [];
    private readonly List<string> _mergeProps = [];
    private readonly List<string> _prependProps = [];
    private readonly List<string> _deepMergeProps = [];
    private readonly List<string> _matchPropsOn = [];
    private readonly List<string> _rescuedProps = [];
    private readonly Dictionary<string, List<string>> _deferredProps = [];
    private readonly Dictionary<string, object?> _scrollProps = [];
    private readonly Dictionary<string, object?> _onceProps = [];

    public PropsResolver(HttpContext context, string component, IServiceProvider services)
    {
        _context = context;
        _component = component;
        _services = services;
        var options = services.GetService<IOptions<InertiaOptions>>()?.Value;
        _jsonOptions = InertiaJsonOptions.GetOptions(options);
        _diagnostics = PropDiagnosticsTrace.Create(context, services, options?.PropDiagnostics);
        _jsonOptions.MakeReadOnly(populateMissingResolver: true);
        var request = context.Request;
        _isInertia = request.Headers.ContainsKey(HeaderNames.Inertia);
        _isPartial = _isInertia && request.Headers[HeaderNames.PartialComponent] == component;
        _only = ParseHeader(request.Headers[HeaderNames.PartialOnly]);
        _except = ParseHeader(request.Headers[HeaderNames.PartialExcept]);
        _resetProps = ParseHeader(request.Headers[HeaderNames.Reset]);
        _loadedOnceProps = ParseHeader(request.Headers[HeaderNames.ExceptOnceProps]);
    }

    public async Task<(Dictionary<string, object?> Props, ResolvedMetadata Metadata)> ResolveAsync(
        Dictionary<string, object?> sharedProps, Dictionary<string, object?> pageProps,
        CancellationToken ct = default)
    {
        var shared = ExpandProviders(sharedProps);
        foreach (var key in shared.Keys)
            AddUnique(_sharedPropKeys, key.Split('.')[0]);
        var merged = new Dictionary<string, object?>(shared);
        foreach (var (key, value) in ExpandProviders(pageProps)) merged[key] = value;
        var resolved = await ResolvePropsAsync(UnpackDotKeys(merged), null, false, ct);
        return (resolved, new ResolvedMetadata(
            NonEmpty(_sharedPropKeys), NonEmpty(_mergeProps), NonEmpty(_prependProps),
            NonEmpty(_deepMergeProps), NonEmpty(_matchPropsOn),
            _deferredProps.Count == 0 ? null : _deferredProps.ToDictionary(
                pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.ToArray()),
            _scrollProps.Count == 0 ? null : _scrollProps,
            _onceProps.Count == 0 ? null : _onceProps,
            NonEmpty(_rescuedProps)));
    }

    private Dictionary<string, object?> ExpandProviders(Dictionary<string, object?> props, string? parent = null)
    {
        var result = new Dictionary<string, object?>();
        foreach (var (key, value) in props)
        {
            if (value is IProvidesInertiaProperties provider)
            {
                using var measurement = _diagnostics?.Begin(parent is null ? key : $"{parent}.{key}", "provider");
                foreach (var (providedKey, providedValue) in provider.ToInertiaProperties(new RenderContext(_component, _context)))
                    result[providedKey] = providedValue;
                measurement?.Complete("resolved");
            }
            else result[key] = value;
        }
        return result;
    }

    private static Dictionary<string, object?> UnpackDotKeys(Dictionary<string, object?> props)
    {
        var result = new Dictionary<string, object?>();
        foreach (var (key, value) in props)
        {
            var parts = key.Split('.');
            var target = result;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (!target.TryGetValue(parts[i], out var existing) || existing is not Dictionary<string, object?> nested)
                    target[parts[i]] = nested = [];
                target = nested;
            }
            target[parts[^1]] = value;
        }
        return result;
    }

    private async Task<Dictionary<string, object?>> ResolvePropsAsync(
        Dictionary<string, object?> props, string? parent, bool parentWasResolved, CancellationToken ct)
    {
        props = ExpandProviders(props, parent);
        var result = new Dictionary<string, object?>();
        foreach (var (key, original) in props)
        {
            ct.ThrowIfCancellationRequested();
            var path = parent is null ? key : $"{parent}.{key}";
            using var measurement = _diagnostics?.Begin(path, PropKind(original));
            try
            {
                if (_isPartial && original is not AlwaysProp && !parentWasResolved && !Included(path, traverseParents: true))
                {
                    measurement?.Complete("excluded");
                    continue;
                }
                var prop = original;
                if (!_isPartial && ExcludeInitial(prop, path))
                {
                    measurement?.Complete(InitialOutcome(prop));
                    continue;
                }
                object? resolved;
                try
                {
                    resolved = await ResolveValueAsync(prop, path, props, ct);
                    // Factories may return a prop wrapper. That wrapper owns its exclusion
                    // rules and metadata, rather than the delegate that produced it.
                    for (var depth = 0; IsWrapper(resolved) && !ReferenceEquals(resolved, prop); depth++)
                    {
                        if (depth == 32) throw new InvalidOperationException($"Too many nested prop wrappers at '{path}'.");
                        prop = resolved;
                        measurement?.Kind(PropKind(prop));
                        if (!_isPartial && ExcludeInitial(prop, path))
                        {
                            measurement?.Complete(InitialOutcome(prop));
                            break;
                        }
                        resolved = await ResolveValueAsync(prop, path, props, ct);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException && prop is IRescuable { ShouldRescue: true })
                {
                    _services.GetService<ILogger<PropsResolver>>()?.LogWarning(ex, "Deferred Inertia prop '{PropPath}' failed.", path);
                    AddUnique(_rescuedProps, path);
                    measurement?.Complete("rescued");
                    continue;
                }
                if (IsWrapper(resolved)) continue; // excluded returned wrapper
                CollectMetadata(prop, path);
                if (prop is ScrollProp scroll)
                {
                    var metadata = await scroll.ResolveMetadataAsync(_services, ct);
                    if (metadata is not null)
                        _scrollProps[path] = new ScrollMetadataValue(metadata.GetPageName(), metadata.GetPreviousPage(),
                            metadata.GetNextPage(), metadata.GetCurrentPage(), _resetProps.Contains(path));
                }
                var resolvedParent = parentWasResolved || PropValueResolver.IsLazyValue(prop) || prop is IProvidesInertiaProperty || IsWrapper(prop);
                result[key] = await ResolveContainerAsync(resolved, path, resolvedParent, ct);
                measurement?.Complete("resolved");
            }
            catch (OperationCanceledException)
            {
                measurement?.Complete("cancelled");
                throw;
            }
        }
        return result;
    }

    private async Task<object?> ResolveContainerAsync(object? value, string path, bool parentWasResolved, CancellationToken ct)
    {
        if (value is null || value is string) return value;
        var contract = _jsonOptions.GetTypeInfo(value.GetType());
        if (contract.Kind == JsonTypeInfoKind.None) return value; // custom converter owns the value
        if (_ancestors.Count >= (_jsonOptions.MaxDepth == 0 ? 64 : _jsonOptions.MaxDepth) || !_ancestors.Add(value))
            throw new JsonException($"A cycle or excessive depth was encountered in Inertia prop '{path}'.");
        try
        {
            if (value is IReadOnlyDictionary<string, object?> readOnly)
                return await ResolvePropsAsync(new(readOnly), path, parentWasResolved, ct);
            if (value is IDictionary dictionary && dictionary.Keys.Cast<object>().All(key => key is string))
            {
                var entries = new Dictionary<string, object?>();
                foreach (DictionaryEntry entry in dictionary) entries[(string)entry.Key] = entry.Value;
                return await ResolvePropsAsync(entries, path, parentWasResolved, ct);
            }
            if (contract.Kind == JsonTypeInfoKind.Dictionary && value is IEnumerable dictionaryEntries)
            {
                var stringKeys = value.GetType().GetInterfaces().Any(type => type.IsGenericType
                    && type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>) && type.GetGenericArguments()[0] == typeof(string));
                if (!stringKeys) return value; // Serializer owns non-string dictionary key conversion.
                var entries = new Dictionary<string, object?>();
                foreach (var entry in dictionaryEntries)
                {
                    var entryType = entry!.GetType();
                    entries[(string)entryType.GetProperty("Key")!.GetValue(entry)!] = entryType.GetProperty("Value")!.GetValue(entry);
                }
                return await ResolvePropsAsync(entries, path, parentWasResolved, ct);
            }
            if (value is IEnumerable sequence && value is not string && value is not IDictionary)
            {
                var result = new List<object?>();
                foreach (var item in sequence)
                {
                    var indexed = await ResolvePropsAsync(new() { [result.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)] = item }, path, parentWasResolved, ct);
                    result.Add(indexed.Values.FirstOrDefault());
                }
                return result;
            }
            if (contract.Kind == JsonTypeInfoKind.Object)
                return await ResolvePropsAsync(PropObjectMapper.ToDictionary(value, _jsonOptions), path, parentWasResolved, ct);
            return value;
        }
        finally { _ancestors.Remove(value); }
    }
    private static bool DescendsFrom(string path, string filter) => path == filter || path.StartsWith(filter + ".", StringComparison.Ordinal);
    private bool Included(string path, bool traverseParents)
        => (_only.Length == 0 || _only.Any(filter => DescendsFrom(path, filter)
            || (traverseParents && DescendsFrom(filter, path))))
            && !_except.Any(filter => DescendsFrom(path, filter));

    private bool AlreadyLoaded(object? prop, string path)
        => _isInertia && prop is IOnceable once && once.ShouldResolveOnce() && !once.ShouldBeRefreshed()
            && _loadedOnceProps.Contains(once.GetKey() ?? path);

    private bool ExcludeInitial(object? prop, string path)
    {
        if (prop is IIgnoreFirstLoad || prop is IDeferrable deferred && deferred.ShouldDefer())
        {
            if (prop is IDeferrable deferrable && deferrable.ShouldDefer() && !AlreadyLoaded(prop, path))
            {
                if (!_deferredProps.TryGetValue(deferrable.Group(), out var paths))
                    _deferredProps[deferrable.Group()] = paths = [];
                AddUnique(paths, path);
            }
            if (prop is ScrollProp scroll) scroll.ConfigureMergeIntent(_context);
            CollectMetadata(prop, path);
            return true;
        }
        if (!AlreadyLoaded(prop, path)) return false;
        CollectOnceMetadata((IOnceable)prop!, path);
        return true;
    }

    private async Task<object?> ResolveValueAsync(object? value, string path, Dictionary<string, object?> siblings, CancellationToken ct)
    {
        if (value is ScrollProp scroll) scroll.ConfigureMergeIntent(_context);
        var resolved = value switch
        {
            AlwaysProp always => await always.ResolveAsync(_services, ct),
            OptionalProp optional => await optional.ResolveAsync(_services, ct),
            DeferProp defer => await defer.ResolveAsync(_services, ct),
            MergeProp merge => await merge.ResolveAsync(_services, ct),
            OnceProp once => await once.ResolveAsync(_services, ct),
            ScrollProp scrollProp => await scrollProp.ResolveAsync(_services, ct),
            IProvidesInertiaProperty provider => provider.ToInertiaProperty(new PropertyContext(path, siblings, _context)),
            Func<IServiceProvider, CancellationToken, Task<object?>> callback => await callback(_services, ct),
            Func<object?> callback => callback(),
            _ => value,
        };
        return await PropValueResolver.ResolveAsync(resolved, _services, ct);
    }

    private static string InitialOutcome(object? prop)
        => prop is IDeferrable deferred && deferred.ShouldDefer() ? "deferred"
            : prop is IIgnoreFirstLoad ? "optional" : "cached";

    private static string PropKind(object? prop) => prop switch
    {
        AlwaysProp => "always", OptionalProp => "optional", DeferProp => "deferred",
        MergeProp => "merge", OnceProp => "once", ScrollProp => "scroll",
        IProvidesInertiaProperty or IProvidesInertiaProperties => "provider",
        Delegate => "callback", _ => "value",
    };

    private static bool IsWrapper(object? value)
        => value is AlwaysProp or OptionalProp or DeferProp or MergeProp or OnceProp or ScrollProp;

    private void CollectMetadata(object? prop, string path)
    {
        if (_isPartial && !Included(path, traverseParents: false)) return;
        if (prop is IMergeable merge && merge.ShouldMerge() && !_resetProps.Contains(path))
        {
            if (merge.ShouldDeepMerge()) AddUnique(_deepMergeProps, path);
            else if (merge.AppendsAtRoot()) AddUnique(_mergeProps, path);
            else if (merge.PrependsAtRoot()) AddUnique(_prependProps, path);
            else
            {
                foreach (var part in merge.AppendsAtPaths()) AddUnique(_mergeProps, $"{path}.{part}");
                foreach (var part in merge.PrependsAtPaths()) AddUnique(_prependProps, $"{path}.{part}");
            }
            foreach (var part in merge.MatchesOn()) AddUnique(_matchPropsOn, $"{path}.{part}");
        }
        if (prop is IOnceable once && once.ShouldResolveOnce()) CollectOnceMetadata(once, path);
    }

    private void CollectOnceMetadata(IOnceable once, string path)
        => _onceProps[once.GetKey() ?? path] = new OnceMetadata(path, once.ExpiresAt());

    private static void AddUnique(List<string> items, string value) { if (!items.Contains(value)) items.Add(value); }
    private static IReadOnlyList<string>? NonEmpty(List<string> items) => items.Count == 0 ? null : items.ToArray();
    private static string[] ParseHeader(Microsoft.Extensions.Primitives.StringValues header)
        => header.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

internal sealed record ResolvedMetadata(
    IReadOnlyList<string>? SharedPropKeys,
    IReadOnlyList<string>? MergeProps,
    IReadOnlyList<string>? PrependProps,
    IReadOnlyList<string>? DeepMergeProps,
    IReadOnlyList<string>? MatchPropsOn,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? DeferredProps,
    IReadOnlyDictionary<string, object?>? ScrollProps,
    IReadOnlyDictionary<string, object?>? OnceProps,
    IReadOnlyList<string>? RescuedProps
);
