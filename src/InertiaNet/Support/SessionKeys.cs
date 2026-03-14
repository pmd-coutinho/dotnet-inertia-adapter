namespace InertiaNet.Support;

/// <summary>
/// TempData / session key constants used by InertiaNet.
/// </summary>
public static class SessionKeys
{
    /// <summary>Signals the client to clear browser history state.</summary>
    public const string ClearHistory = "inertia.clear_history";

    /// <summary>Inertia flash data (separate from ASP.NET Core's default TempData).</summary>
    public const string FlashData = "inertia.flash_data";

    /// <summary>Signals the client to preserve the URL fragment across a redirect.</summary>
    public const string PreserveFragment = "inertia.preserve_fragment";

    /// <summary>Validation errors forwarded across a redirect via TempData.</summary>
    public const string ValidationErrors = "inertia.validation_errors";

    /// <summary>Named validation error bag (from X-Inertia-Error-Bag header).</summary>
    public const string ErrorBag = "inertia.error_bag";
}
