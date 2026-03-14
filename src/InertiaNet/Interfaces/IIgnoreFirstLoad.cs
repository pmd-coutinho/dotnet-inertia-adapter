namespace InertiaNet.Interfaces;

/// <summary>
/// Marker interface. Props implementing this interface are excluded from
/// the initial (non-partial) page response. They are only included when
/// explicitly requested via a partial reload.
/// <para>Implemented by: <see cref="Props.OptionalProp"/>, <see cref="Props.DeferProp"/>.</para>
/// </summary>
public interface IIgnoreFirstLoad { }
