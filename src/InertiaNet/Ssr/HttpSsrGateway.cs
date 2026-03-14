using InertiaNet.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace InertiaNet.Ssr;

/// <summary>
/// Dispatches Inertia page objects to a Node.js SSR server via HTTP POST.
/// Supports Vite hot-mode (routes to Vite dev server) and production mode.
/// Gracefully falls back to CSR on any failure.
/// </summary>
internal sealed class HttpSsrGateway : ISsrGateway
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<InertiaOptions> _options;
    private readonly ILogger<HttpSsrGateway> _logger;

    private const string ClientName = "InertiaNet.SSR";

    public HttpSsrGateway(
        IHttpClientFactory httpClientFactory,
        IOptions<InertiaOptions> options,
        ILogger<HttpSsrGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<SsrResponse?> DispatchAsync(InertiaPage page, CancellationToken ct = default)
    {
        var ssr = _options.Value.Ssr;

        if (!ssr.Enabled)
            return null;

        try
        {
            var client = _httpClientFactory.CreateClient(ClientName);
            var endpoint = GetRenderEndpoint(ssr);

            using var response = await client.PostAsJsonAsync(
                endpoint,
                page,
                InertiaJsonOptions.Default,
                ct);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SsrResponseDto>(ct);
            if (result is null)
                return null;

            // SSR server returns { head: string|string[], body: string }
            // JsonElement can be either a JSON string or a JSON array
            string head;
            if (result.Head is System.Text.Json.JsonElement elem)
            {
                head = elem.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? string.Join("\n", elem.EnumerateArray().Select(e => e.GetString() ?? string.Empty))
                    : elem.GetString() ?? string.Empty;
            }
            else
            {
                head = string.Empty;
            }
            return new SsrResponse(head, result.Body ?? string.Empty);
        }
        catch (HttpRequestException ex)
        {
            return HandleFailure(SsrErrorType.Connection, ex, ssr.ThrowOnError, page);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return HandleFailure(SsrErrorType.Connection, ex, ssr.ThrowOnError, page);
        }
        catch (JsonException ex)
        {
            return HandleFailure(SsrErrorType.Render, ex, ssr.ThrowOnError, page);
        }
        catch (Exception ex)
        {
            return HandleFailure(SsrErrorType.Unknown, ex, ssr.ThrowOnError, page);
        }
    }

    private static string GetRenderEndpoint(SsrOptions ssr)
    {
        // Vite hot mode: check for a "hot" file at wwwroot/hot
        // (same pattern as Laravel's Vite::isRunningHot())
        var hotFile = Path.Combine("wwwroot", "hot");
        if (File.Exists(hotFile))
        {
            var viteUrl = File.ReadAllText(hotFile).Trim();
            return $"{viteUrl.TrimEnd('/')}/__inertia_ssr";
        }

        // Use the configured SSR server URL (default: http://127.0.0.1:13714)
        return $"{ssr.Url.TrimEnd('/')}/render";
    }

    private SsrResponse? HandleFailure(
        SsrErrorType errorType, Exception ex, bool throwOnError, InertiaPage page)
    {
        _logger.LogWarning(
            ex,
            "Inertia SSR rendering failed [{ErrorType}] for component '{Component}'. Falling back to CSR.",
            errorType,
            page.Component);

        if (throwOnError)
            throw new SsrException(errorType, page.Component, ex);

        return null;
    }

    // DTO for deserialising the SSR server response
    private sealed class SsrResponseDto
    {
        public object? Head { get; set; }   // can be string or string[]
        public string? Body { get; set; }
    }
}

/// <summary>
/// Thrown when SSR rendering fails and <c>SsrOptions.ThrowOnError</c> is true.
/// </summary>
public sealed class SsrException : Exception
{
    public SsrErrorType ErrorType { get; }
    public string? Component { get; }

    public SsrException(SsrErrorType errorType, string? component, Exception innerException)
        : base($"Inertia SSR rendering failed [{errorType}] for component '{component}'.", innerException)
    {
        ErrorType = errorType;
        Component = component;
    }
}
