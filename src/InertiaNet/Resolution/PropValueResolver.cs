using System.Reflection;
using System.Runtime.ExceptionServices;

namespace InertiaNet.Resolution;

/// <summary>Unwraps typed factories without eager evaluation of excluded props.</summary>
internal static class PropValueResolver
{
    public static bool IsLazyValue(object? value)
        => value is Delegate or Task || value is not null && value.GetType().IsGenericType
            && (value.GetType().GetGenericTypeDefinition() == typeof(Lazy<>) || value.GetType().GetGenericTypeDefinition() == typeof(ValueTask<>));

    public static async Task<object?> ResolveAsync(object? value, IServiceProvider services, CancellationToken ct)
    {
        for (var depth = 0; depth < 32; depth++)
        {
            ct.ThrowIfCancellationRequested();
            if (value is Task task)
            {
                await task.WaitAsync(ct);
                var result = task.GetType().GetProperty("Result");
                value = result?.PropertyType.FullName == "System.Threading.Tasks.VoidTaskResult" ? null : result?.GetValue(task);
                continue;
            }
            if (value is Delegate callback)
            {
                var parameters = callback.Method.GetParameters();
                if (callback.Method.ReturnType == typeof(void)) throw new InvalidOperationException("Inertia prop factories must return a value.");
                value = parameters.Length switch
                {
                    0 => Invoke(() => callback.DynamicInvoke()),
                    2 when parameters[0].ParameterType == typeof(IServiceProvider) && parameters[1].ParameterType == typeof(CancellationToken)
                        => Invoke(() => callback.DynamicInvoke(services, ct)),
                    _ => throw new InvalidOperationException("Inertia prop factories must take no arguments, or IServiceProvider and CancellationToken."),
                };
                continue;
            }
            var type = value?.GetType();
            if (type?.IsGenericType == true && type.GetGenericTypeDefinition() == typeof(Lazy<>))
            {
                value = Invoke(() => type.GetProperty("Value")!.GetValue(value));
                continue;
            }
            if (type?.IsGenericType == true && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
            {
                value = Invoke(() => type.GetMethod("AsTask")!.Invoke(value, null));
                continue;
            }
            return value;
        }
        throw new InvalidOperationException("Too many nested lazy Inertia prop values.");
    }

    private static object? Invoke(Func<object?> action)
    {
        try { return action(); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}
