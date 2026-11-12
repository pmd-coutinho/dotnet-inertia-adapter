namespace InertiaNet.Core;

/// <summary>
/// Controls how validation error keys are written to the <c>errors</c> prop when errors are
/// forwarded across a redirect (MVC <c>ModelState</c> and <c>SetInertiaValidationErrors</c>).
/// </summary>
/// <remarks>
/// <see cref="Brackets"/> and <see cref="Dots"/> apply the property naming policy to every
/// member segment of a key (<c>Address.Street</c> becomes <c>address.street</c>). Precognition
/// responses always use <see cref="Dots"/>, which is the path form sent by the Precognition client.
/// </remarks>
public enum ValidationErrorKeyFormat
{
    /// <summary>
    /// Keeps ASP.NET Core indexers in brackets, for example <c>values[3].code</c> and
    /// <c>prices[eur].amount</c>. This is the default and matches the indexer form forwarded by earlier releases.
    /// </summary>
    Brackets,

    /// <summary>
    /// Writes indexers as dot segments, for example <c>values.3.code</c>. This matches the keys emitted by
    /// Laravel and the dot paths typed by the Inertia <c>useForm</c> helper.
    /// </summary>
    Dots,

    /// <summary>
    /// Forwards keys exactly as produced by ModelState or supplied to <c>SetInertiaValidationErrors</c>,
    /// without applying a naming policy or rewriting indexers.
    /// </summary>
    Preserve,
}
