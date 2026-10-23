namespace InertiaNet.Ssr;

/// <summary>
/// The SSR response returned by the Node.js SSR server.
/// Contains the rendered HTML head content and body content.
/// </summary>
/// <param name="Head">HTML to inject into the document head.</param>
/// <param name="Body">Rendered HTML, including the official SSR root and page script.</param>
public sealed record SsrResponse(string Head, string Body);
