namespace InertiaNet.Props;

/// <summary>
/// A prop that is always included in every Inertia response, even partial reloads
/// that would normally filter it out. Equivalent to Laravel's <c>Inertia::always()</c>.
/// </summary>
public sealed class AlwaysProp
{
    private readonly object? _value;
    private readonly Func<IServiceProvider, CancellationToken, Task<object?>>? _callback;

    public AlwaysProp(object? value)
    {
        _value = value;
    }

    public AlwaysProp(Func<IServiceProvider, CancellationToken, Task<object?>> callback)
    {
        _callback = callback;
    }

    public async Task<object?> ResolveAsync(IServiceProvider services, CancellationToken ct = default)
    {
        if (_callback is not null)
            return await _callback(services, ct);

        return _value;
    }
}
