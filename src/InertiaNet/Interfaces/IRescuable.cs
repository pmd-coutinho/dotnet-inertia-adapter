namespace InertiaNet.Interfaces;

/// <summary>A prop that can report a failure without failing the entire page.</summary>
public interface IRescuable
{
    /// <summary>Whether non-cancellation failures should be reported as rescued props.</summary>
    bool ShouldRescue { get; }
}
