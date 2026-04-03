using InertiaNet.Core;
using InertiaNet.Support;
using InertiaNet.Testing;
using System.Net;
using System.Text.Json;

namespace InertiaNet.Testing;

/// <summary>
/// Extension methods for testing Inertia responses via <see cref="HttpResponseMessage"/>.
/// </summary>
public static class InertiaTestExtensions
{
    /// <summary>
    /// Asserts the response is a successful Inertia JSON response and returns
    /// an <see cref="AssertableInertiaPage"/> for fluent assertions.
    /// </summary>
    /// <exception cref="AssertionException">Thrown if the response is not an Inertia JSON response.</exception>
    public static async Task<AssertableInertiaPage> AssertInertiaAsync(this HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.OK)
            throw new AssertionException($"Expected status 200, got {(int)response.StatusCode}.");

        if (!response.Headers.TryGetValues(HeaderNames.Inertia, out var inertiaHeader)
            || !string.Equals(inertiaHeader.FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new AssertionException($"Expected {HeaderNames.Inertia}: true response header.");
        }

        if (!response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
            throw new AssertionException($"Expected JSON content type, got '{response.Content.Headers.ContentType?.MediaType}'.");

        var json = await response.Content.ReadAsStringAsync();
        var page = JsonSerializer.Deserialize<InertiaPage>(json, InertiaJsonOptions.Default);

        if (page is null)
            throw new AssertionException("Response is not a valid Inertia page JSON.");

        return new AssertableInertiaPage(page);
    }

    /// <summary>
    /// Asserts the response is an Inertia redirect (409 Conflict with X-Inertia-Location).
    /// </summary>
    public static AssertableInertiaRedirect AssertInertiaRedirect(this HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Conflict)
            throw new AssertionException($"Expected status 409, got {(int)response.StatusCode}.");

        if (!response.Headers.TryGetValues(HeaderNames.Location, out var locationHeader))
            throw new AssertionException($"Expected {HeaderNames.Location} header.");

        var location = locationHeader.FirstOrDefault() ?? string.Empty;
        return new AssertableInertiaRedirect(location);
    }

    /// <summary>
    /// Asserts the response is a fragment-bearing redirect (409 with X-Inertia-Redirect).
    /// </summary>
    public static AssertableInertiaFragmentRedirect AssertFragmentRedirect(this HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Conflict)
            throw new AssertionException($"Expected status 409, got {(int)response.StatusCode}.");

        if (!response.Headers.TryGetValues(HeaderNames.Redirect, out var redirectHeader))
            throw new AssertionException($"Expected {HeaderNames.Redirect} header.");

        var redirect = redirectHeader.FirstOrDefault() ?? string.Empty;
        return new AssertableInertiaFragmentRedirect(redirect);
    }

    /// <summary>
    /// Asserts the response is a version-mismatch redirect (409 Conflict with X-Inertia-Location).
    /// </summary>
    public static AssertableInertiaVersionRedirect AssertVersionRedirect(this HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Conflict)
            throw new AssertionException($"Expected status 409, got {(int)response.StatusCode}.");

        if (!response.Headers.TryGetValues(HeaderNames.Location, out var locationHeader))
            throw new AssertionException($"Expected {HeaderNames.Location} header.");

        var location = locationHeader.FirstOrDefault() ?? string.Empty;
        return new AssertableInertiaVersionRedirect(location);
    }
}

/// <summary>Assertion wrapper for Inertia redirect responses.</summary>
public sealed class AssertableInertiaRedirect
{
    public string Location { get; }
    public AssertableInertiaRedirect(string location) => Location = location;

    public AssertableInertiaRedirect To(string expected)
    {
        if (Location != expected)
            throw new AssertionException($"Expected redirect to '{expected}', got '{Location}'.");
        return this;
    }
}

/// <summary>Assertion wrapper for fragment-bearing redirects.</summary>
public sealed class AssertableInertiaFragmentRedirect
{
    public string RedirectUrl { get; }
    public AssertableInertiaFragmentRedirect(string url) => RedirectUrl = url;

    public AssertableInertiaFragmentRedirect To(string expected)
    {
        if (RedirectUrl != expected)
            throw new AssertionException($"Expected fragment redirect to '{expected}', got '{RedirectUrl}'.");
        return this;
    }
}

/// <summary>Assertion wrapper for version-mismatch redirects.</summary>
public sealed class AssertableInertiaVersionRedirect
{
    public string RedirectUrl { get; }
    public AssertableInertiaVersionRedirect(string url) => RedirectUrl = url;

    public AssertableInertiaVersionRedirect To(string expected)
    {
        if (RedirectUrl != expected)
            throw new AssertionException($"Expected version redirect to '{expected}', got '{RedirectUrl}'.");
        return this;
    }
}
