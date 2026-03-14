namespace InertiaNet.Interfaces;

/// <summary>
/// Marks a prop as mergeable — its resolved value is merged with existing
/// client-side data rather than replacing it.
/// </summary>
public interface IMergeable
{
    bool ShouldMerge();
    bool ShouldDeepMerge();

    /// <summary>Keys used to match items when deduplicating during a merge.</summary>
    IReadOnlyList<string> MatchesOn();

    /// <summary>When true, the resolved value is appended at the root level.</summary>
    bool AppendsAtRoot();

    /// <summary>When true, the resolved value is prepended at the root level.</summary>
    bool PrependsAtRoot();

    /// <summary>Nested paths at which to append the value.</summary>
    IReadOnlyList<string> AppendsAtPaths();

    /// <summary>Nested paths at which to prepend the value.</summary>
    IReadOnlyList<string> PrependsAtPaths();
}
