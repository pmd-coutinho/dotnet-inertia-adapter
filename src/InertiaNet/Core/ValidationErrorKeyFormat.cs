namespace InertiaNet.Core;

/// <summary>
/// Controls how validation error keys are written to the <c>errors</c> prop when errors are
/// forwarded across a redirect (MVC <c>ModelState</c> and <c>SetInertiaValidationErrors</c>).
/// </summary>
/// <remarks>
/// <see cref="Brackets"/> and <see cref="Dots"/> apply the property naming policy props are serialized with
/// to every member segment of a key (<c>Address.Street</c> becomes <c>address.street</c> under the default
/// camelCase policy). Precognition responses always use <see cref="Dots"/>, which is the path form sent by the
/// Precognition client and the default for forwarded errors. The member order (and so the numeric values) is
/// unchanged from earlier releases, so <c>default(ValidationErrorKeyFormat)</c> is still <see cref="Brackets"/>;
/// the default of <see cref="InertiaOptions.ValidationErrorKeyFormat"/> is <see cref="Dots"/>.
/// </remarks>
public enum ValidationErrorKeyFormat
{
    /// <summary>
    /// Keeps ASP.NET Core indexers in brackets, for example <c>values[3].code</c> and
    /// <c>prices[EUR].amount</c> (dictionary keys keep their casing unless a
    /// <c>DictionaryKeyPolicy</c> is configured). This matches the indexer form forwarded by default by earlier
    /// releases; Precognition responses still use dot paths.
    /// </summary>
    Brackets,

    /// <summary>
    /// Writes indexers as dot segments, for example <c>values.3.code</c> and <c>prices.EUR.amount</c>. This is the
    /// default. It matches the keys emitted by Laravel, the dot paths typed by the Inertia <c>useForm</c> helper
    /// and the keys of Precognition responses.
    /// </summary>
    Dots,

    /// <summary>
    /// Forwards keys exactly as produced by ModelState or supplied to <c>SetInertiaValidationErrors</c>,
    /// without applying a naming policy or rewriting indexers.
    /// </summary>
    Preserve,
}
