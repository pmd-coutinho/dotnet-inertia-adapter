namespace InertiaNet.Core;

/// <summary>A named frontend page whose props are checked by the C# compiler.</summary>
/// <typeparam name="TProps">The page's serialized props contract.</typeparam>
public sealed record InertiaComponent<TProps>
{
    /// <summary>The component name resolved by the frontend adapter.</summary>
    public string Name { get; }

    /// <summary>Declares a component and its props contract.</summary>
    public InertiaComponent(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }
}
