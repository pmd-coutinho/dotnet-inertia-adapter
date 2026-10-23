namespace InertiaNet.Core;

/// <summary>
/// Declares the resolved wire type of an object-typed or nongeneric prop wrapper for Pathfinder.
/// This is a generation hint; callbacks and serialization remain authoritative at runtime.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class InertiaPropTypeAttribute : Attribute
{
    /// <summary>The type produced after resolving this prop.</summary>
    public Type Type { get; }

    /// <summary>Declares the type produced by this prop.</summary>
    public InertiaPropTypeAttribute(Type type) => Type = type ?? throw new ArgumentNullException(nameof(type));
}
