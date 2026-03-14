namespace InertiaNet.Ssr;

/// <summary>
/// The SSR response returned by the Node.js SSR server.
/// Contains the rendered HTML head content and body content.
/// </summary>
public sealed record SsrResponse(
    /// <summary>HTML to inject into &lt;head&gt; (e.g. &lt;title&gt;, &lt;meta&gt;).</summary>
    string Head,

    /// <summary>Rendered HTML for the application body.</summary>
    string Body
);
