using InertiaNet.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace InertiaNet.Ssr;

/// <summary>Dispatches page objects to Node/Vite SSR with bounded CSR fallback.</summary>
internal sealed class HttpSsrGateway(
    IHttpClientFactory httpClientFactory,
    IOptions<InertiaOptions> options,
    IOptions<ViteOptions> viteOptions,
    ILogger<HttpSsrGateway> logger) : ISsrGateway
{
    public async Task<SsrResponse?> DispatchAsync(InertiaPage page, CancellationToken ct = default)
    {
        var ssr = options.Value.Ssr;
        if (!ssr.Enabled) return null;
        ct.ThrowIfCancellationRequested();
        var started = Stopwatch.GetTimestamp();
        using var activity = InertiaSsr.Activities.StartActivity("inertia.ssr.render", ActivityKind.Client);
        activity?.SetTag("inertia.component", page.Component);
        var outcome = "success";
        SsrErrorType? errorType = null;
        SsrResponse? Fail(SsrErrorType type, Exception exception)
        {
            errorType = type;
            outcome = ssr.ThrowOnError ? "failure" : "fallback";
            activity?.SetStatus(ActivityStatusCode.Error);
            logger.LogWarning(exception,
                "Inertia SSR rendering failed [{ErrorType}] for component '{Component}'.", type, page.Component);
            if (ssr.ThrowOnError) throw new SsrException(type, page.Component, exception);
            return null;
        }
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (ssr.Timeout != Timeout.InfiniteTimeSpan) timeout.CancelAfter(ssr.Timeout);
            using var client = httpClientFactory.CreateClient(InertiaSsr.HttpClientName);
            using var response = await client.PostAsJsonAsync(GetRenderEndpoint(ssr, viteOptions.Value),
                page, InertiaJsonOptions.GetOptions(options.Value), timeout.Token);
            activity?.SetTag("http.response.status_code", (int)response.StatusCode);
            if (!response.IsSuccessStatusCode)
            {
                SsrErrorDto? error = null;
                try { error = await response.Content.ReadFromJsonAsync<SsrErrorDto>(timeout.Token); }
                catch (JsonException) { }
                var type = error?.Type switch
                {
                    "browser-api" => SsrErrorType.BrowserApi,
                    "component-resolution" => SsrErrorType.ComponentResolution,
                    "render" => SsrErrorType.Render,
                    "connection" => SsrErrorType.Connection,
                    _ => SsrErrorType.Unknown,
                };
                return Fail(type, new HttpRequestException(error?.Error ?? $"SSR returned HTTP {(int)response.StatusCode}.", null, response.StatusCode));
            }
            var result = await response.Content.ReadFromJsonAsync<SsrResponseDto>(timeout.Token);
            if (result?.Body is null) throw new JsonException("SSR response must contain a string body.");
            var head = result.Head.ValueKind switch
            {
                JsonValueKind.Array => string.Join("\n", result.Head.EnumerateArray().Select(ReadHead)),
                JsonValueKind.String => result.Head.GetString()!,
                JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                _ => throw new JsonException("SSR head must be a string or an array of strings."),
            };
            return new(head, result.Body);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            outcome = "cancelled";
            throw;
        }
        catch (OperationCanceledException ex) { return Fail(SsrErrorType.Timeout, ex); }
        catch (SsrException) { throw; }
        catch (HttpRequestException ex) { return Fail(SsrErrorType.Connection, ex); }
        catch (JsonException ex) { return Fail(SsrErrorType.Render, ex); }
        catch (Exception ex) { return Fail(SsrErrorType.Unknown, ex); }
        finally
        {
            var tags = new TagList { { "outcome", outcome } };
            activity?.SetTag("inertia.ssr.outcome", outcome);
            if (errorType is { } type)
            {
                var name = InertiaSsr.ErrorName(type);
                tags.Add("error.type", name);
                activity?.SetTag("error.type", name);
            }
            InertiaSsr.Renders.Add(1, tags);
            InertiaSsr.Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
        }
    }

    private static string ReadHead(JsonElement element)
        => element.ValueKind == JsonValueKind.String ? element.GetString()!
            : throw new JsonException("SSR head entries must be strings.");

    private static string GetRenderEndpoint(SsrOptions ssr, ViteOptions viteOptions)
    {
        var hotFile = Path.Combine(viteOptions.PublicDirectory, viteOptions.HotFile);
        if (File.Exists(hotFile)) return $"{File.ReadAllText(hotFile).Trim().TrimEnd('/')}/__inertia_ssr";
        return $"{ssr.Url.TrimEnd('/')}/render";
    }

    private sealed class SsrResponseDto
    {
        public JsonElement Head { get; set; }
        public string? Body { get; set; }
    }

    private sealed class SsrErrorDto
    {
        public string? Type { get; set; }
        public string? Error { get; set; }
    }
}

/// <summary>Thrown on SSR failure when <c>SsrOptions.ThrowOnError</c> is enabled.</summary>
public sealed class SsrException : Exception
{
    /// <summary>The classified failure type.</summary>
    public SsrErrorType ErrorType { get; }
    /// <summary>The requested component.</summary>
    public string? Component { get; }

    /// <summary>Creates a classified SSR exception.</summary>
    public SsrException(SsrErrorType errorType, string? component, Exception innerException)
        : base($"Inertia SSR rendering failed [{errorType}] for component '{component}'.", innerException)
    {
        ErrorType = errorType;
        Component = component;
    }
}
