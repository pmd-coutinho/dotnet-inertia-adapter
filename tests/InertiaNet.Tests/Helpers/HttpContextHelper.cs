using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;

namespace InertiaNet.Tests.Helpers;

public static class HttpContextHelper
{
    /// <summary>
    /// Creates a DefaultHttpContext with configurable method, path, headers, and DI services.
    /// Response body is a MemoryStream for capturing output.
    /// </summary>
    public static DefaultHttpContext Create(
        string method = "GET",
        string path = "/",
        Dictionary<string, string>? headers = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        configureServices?.Invoke(services);
        var sp = services.BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = sp,
        };

        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        if (headers is not null)
        {
            foreach (var (key, value) in headers)
                context.Request.Headers[key] = value;
        }

        return context;
    }

    /// <summary>
    /// Creates an HttpContext configured for Inertia requests (with X-Inertia header).
    /// </summary>
    public static DefaultHttpContext CreateInertia(
        string method = "GET",
        string path = "/",
        string? version = null,
        string? partialComponent = null,
        string? partialOnly = null,
        string? partialExcept = null,
        string? exceptOnceProps = null,
        Dictionary<string, string>? extraHeaders = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var headers = new Dictionary<string, string>
        {
            ["X-Inertia"] = "true"
        };

        if (version is not null) headers["X-Inertia-Version"] = version;
        if (partialComponent is not null) headers["X-Inertia-Partial-Component"] = partialComponent;
        if (partialOnly is not null) headers["X-Inertia-Partial-Data"] = partialOnly;
        if (partialExcept is not null) headers["X-Inertia-Partial-Except"] = partialExcept;
        if (exceptOnceProps is not null) headers["X-Inertia-Except-Once-Props"] = exceptOnceProps;

        if (extraHeaders is not null)
            foreach (var (k, v) in extraHeaders)
                headers[k] = v;

        return Create(method, path, headers, configureServices);
    }

    /// <summary>
    /// Reads the response body as a string (resets stream position).
    /// </summary>
    public static async Task<string> ReadResponseBodyAsync(this HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}
