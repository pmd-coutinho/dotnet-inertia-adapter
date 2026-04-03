using InertiaNet.Interfaces;
using InertiaNet.Props;
using InertiaNet.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace InertiaNet.Core;

/// <inheritdoc cref="IInertiaService"/>
internal sealed class InertiaService : IInertiaService
{
    private readonly IOptions<InertiaOptions> _options;
    private readonly IHttpContextAccessor _httpContextAccessor;

    // Per-request accumulated state
    private readonly Dictionary<string, object?> _sharedProps = [];
    private readonly Dictionary<string, object?> _flashData = [];
    private readonly List<string> _ssrExcludedPaths = [];
    private bool _clearHistory;
    private bool _preserveFragment;
    private bool? _encryptHistory;

    public InertiaService(IOptions<InertiaOptions> options, IHttpContextAccessor httpContextAccessor)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor;
    }

    // ── Shared props ─────────────────────────────────────────────────────────

    public IInertiaService Share(string key, object? value) { _sharedProps[key] = value; return this; }
    public IInertiaService Share(IDictionary<string, object?> data) { foreach (var kv in data) _sharedProps[kv.Key] = kv.Value; return this; }
    public IInertiaService Share(IProvidesInertiaProperties provider) { _sharedProps[Guid.NewGuid().ToString()] = provider; return this; }
    public IReadOnlyDictionary<string, object?> GetSharedProps() => _sharedProps;

    public OnceProp ShareOnce(string key, Func<IServiceProvider, CancellationToken, Task<object?>> callback)
    {
        var prop = new OnceProp(callback);
        _sharedProps[key] = prop;
        return prop;
    }

    public OnceProp ShareOnce(string key, object? value)
    {
        var prop = new OnceProp((_, _) => Task.FromResult(value));
        _sharedProps[key] = prop;
        return prop;
    }

    // ── Prop factories ────────────────────────────────────────────────────────

    public AlwaysProp Always(object? value) => new(value);
    public AlwaysProp Always(Func<IServiceProvider, CancellationToken, Task<object?>> callback) => new(callback);
    public OptionalProp Optional(Func<IServiceProvider, CancellationToken, Task<object?>> callback) => new(callback);
    public DeferProp Defer(Func<IServiceProvider, CancellationToken, Task<object?>> callback, string? group = null) => new(callback, group);
    public MergeProp Merge(object? value) => new(value);
    public MergeProp Merge(Func<IServiceProvider, CancellationToken, Task<object?>> callback) => new(callback);
    public MergeProp DeepMerge(object? value) => new(value, deepMerge: true);
    public MergeProp DeepMerge(Func<IServiceProvider, CancellationToken, Task<object?>> callback) => new(callback, deepMerge: true);
    public OnceProp Once(Func<IServiceProvider, CancellationToken, Task<object?>> callback) => new(callback);

    public ScrollProp Scroll(object? value, string wrapper = "data", IProvidesScrollMetadata? metadata = null)
        => new(value, wrapper, metadata);

    public ScrollProp Scroll(
        Func<IServiceProvider, CancellationToken, Task<object?>> callback,
        string wrapper = "data",
        Func<IServiceProvider, CancellationToken, Task<IProvidesScrollMetadata?>>? metadata = null)
        => new(callback, wrapper, metadata);

    // ── Conditional props ──────────────────────────────────────────────────────

    public IInertiaService When(bool condition, string key, object? value)
        => condition ? Share(key, value) : this;

    public IInertiaService When(bool condition, string key, Func<IServiceProvider, CancellationToken, Task<object?>> callback)
        => condition ? Share(key, (object?)callback) : this;

    public IInertiaService Unless(bool condition, string key, object? value)
        => !condition ? Share(key, value) : this;

    public IInertiaService Unless(bool condition, string key, Func<IServiceProvider, CancellationToken, Task<object?>> callback)
        => !condition ? Share(key, (object?)callback) : this;

    // ── Rendering ─────────────────────────────────────────────────────────────

    public InertiaResult Render(string component, object? props = null)
    {
        var propDict = props is IDictionary<string, object?> d
            ? d
            : ObjectToDictionary(props);
        return new InertiaResult(component, new Dictionary<string, object?>(_sharedProps), propDict, this, _options);
    }

    public InertiaResult Render(string component, IDictionary<string, object?> props)
        => new(component, new Dictionary<string, object?>(_sharedProps), props, this, _options);

    // ── Navigation ────────────────────────────────────────────────────────────

    public IResult Location(string url)
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx is not null && ctx.Request.Headers.ContainsKey(HeaderNames.Inertia))
        {
            return new InertiaLocationResult(url);
        }
        return Results.Redirect(url);
    }

    // ── Flags ─────────────────────────────────────────────────────────────────

    public IInertiaService ClearHistory() { _clearHistory = true; return this; }
    public IInertiaService PreserveFragment() { _preserveFragment = true; return this; }
    public IInertiaService EncryptHistory(bool encrypt = true) { _encryptHistory = encrypt; return this; }
    public bool GetClearHistory() => _clearHistory;
    public bool GetPreserveFragment() => _preserveFragment;
    public bool? GetEncryptHistory() => _encryptHistory;

    // ── Flash data ────────────────────────────────────────────────────────────

    public IInertiaService Flash(string key, object? value) { _flashData[key] = value; return this; }
    public IInertiaService Flash(IDictionary<string, object?> data) { foreach (var kv in data) _flashData[kv.Key] = kv.Value; return this; }
    public IReadOnlyDictionary<string, object?> GetFlashData() => _flashData;

    // ── SSR exclusions ────────────────────────────────────────────────────────

    public IInertiaService WithoutSsr(params string[] paths)
    {
        if (paths.Length == 0)
        {
            var currentPath = _httpContextAccessor.HttpContext?.Request.Path.Value;
            if (!string.IsNullOrWhiteSpace(currentPath))
                _ssrExcludedPaths.Add(currentPath);

            return this;
        }

        _ssrExcludedPaths.AddRange(paths.Where(path => !string.IsNullOrWhiteSpace(path)));
        return this;
    }

    public IReadOnlyList<string> GetSsrExcludedPaths() => _ssrExcludedPaths;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> ObjectToDictionary(object? obj)
    {
        if (obj is null) return [];
        var result = new Dictionary<string, object?>();
        foreach (var prop in obj.GetType().GetProperties())
            result[JsonNamingPolicy.CamelCase.ConvertName(prop.Name)] = prop.GetValue(obj);
        return result;
    }
}

/// <summary>
/// An Inertia external redirect — returns 409 Conflict with X-Inertia-Location header.
/// This signals the Inertia client to perform a full-page navigation to the given URL.
/// </summary>
internal sealed class InertiaLocationResult : IResult
{
    private readonly string _url;
    public InertiaLocationResult(string url) => _url = url;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        httpContext.Response.Headers[HeaderNames.Location] = _url;
        return Task.CompletedTask;
    }
}
