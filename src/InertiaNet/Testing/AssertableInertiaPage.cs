using InertiaNet.Core;
using System.Text.Json;

namespace InertiaNet.Testing;

/// <summary>
/// Fluent assertion wrapper for <see cref="InertiaPage"/> responses.
/// Enables readable assertions in integration tests.
/// </summary>
public sealed class AssertableInertiaPage
{
    private readonly InertiaPage _page;

    public AssertableInertiaPage(InertiaPage page) => _page = page;

    /// <summary>Returns a new assertion wrapper from an Inertia JSON response.</summary>
    public static AssertableInertiaPage FromJson(string json)
    {
        var page = JsonSerializer.Deserialize<InertiaPage>(json, InertiaJsonOptions.Default)
            ?? throw new InvalidOperationException("Failed to deserialize Inertia page JSON.");
        return new AssertableInertiaPage(page);
    }

    /// <summary>Asserts the component name matches the expected value.</summary>
    public AssertableInertiaPage HasComponent(string expected)
    {
        if (_page.Component != expected)
            throw new AssertionException($"Expected component '{expected}', got '{_page.Component}'.");
        return this;
    }

    /// <summary>Asserts a prop exists (with optional value check).</summary>
    public AssertableInertiaPage HasProp(string key, object? expectedValue = null)
    {
        if (!_page.Props.TryGetValue(key, out var actualValue))
            throw new AssertionException($"Prop '{key}' does not exist.");

        if (expectedValue is not null && !JsonSerializer.Serialize(actualValue).Contains(JsonSerializer.Serialize(expectedValue)))
            throw new AssertionException($"Prop '{key}' expected value '{expectedValue}', got '{actualValue}'.");
        return this;
    }

    /// <summary>Asserts a prop does NOT exist.</summary>
    public AssertableInertiaPage DoesNotHaveProp(string key)
    {
        if (_page.Props.ContainsKey(key))
            throw new AssertionException($"Prop '{key}' should not exist.");
        return this;
    }

    /// <summary>Asserts a shared prop exists (from <c>Share()</c>).</summary>
    public AssertableInertiaPage HasSharedProp(string key)
    {
        if (_page.SharedProps is null || !_page.SharedProps.Contains(key))
            throw new AssertionException($"Shared prop '{key}' not found.");
        return this;
    }

    /// <summary>Asserts the URL matches the expected value.</summary>
    public AssertableInertiaPage HasUrl(string expected)
    {
        if (_page.Url != expected)
            throw new AssertionException($"Expected URL '{expected}', got '{_page.Url}'.");
        return this;
    }

    /// <summary>Asserts the version string matches.</summary>
    public AssertableInertiaPage HasVersion(string expected)
    {
        if (_page.Version != expected)
            throw new AssertionException($"Expected version '{expected}', got '{_page.Version}'.");
        return this;
    }

    /// <summary>Asserts no version is set.</summary>
    public AssertableInertiaPage HasNoVersion()
    {
        if (_page.Version is not null)
            throw new AssertionException($"Expected no version, got '{_page.Version}'.");
        return this;
    }

    /// <summary>Asserts the page has deferred props.</summary>
    public AssertableInertiaPage HasDeferredProps()
    {
        if (_page.DeferredProps is null || _page.DeferredProps.Count == 0)
            throw new AssertionException("Expected deferred props, but none were found.");
        return this;
    }

    /// <summary>Asserts the page has the specified deferred prop group.</summary>
    public AssertableInertiaPage HasDeferredPropGroup(string groupName)
    {
        if (_page.DeferredProps is null || !_page.DeferredProps.ContainsKey(groupName))
            throw new AssertionException($"Deferred prop group '{groupName}' not found.");
        return this;
    }

    /// <summary>Asserts the clearHistory flag is set.</summary>
    public AssertableInertiaPage HasClearHistory()
    {
        if (_page.ClearHistory != true)
            throw new AssertionException("Expected clearHistory to be true.");
        return this;
    }

    /// <summary>Asserts the encryptHistory flag is set.</summary>
    public AssertableInertiaPage HasEncryptHistory()
    {
        if (_page.EncryptHistory != true)
            throw new AssertionException("Expected encryptHistory to be true.");
        return this;
    }

    /// <summary>Asserts the preserveFragment flag is set.</summary>
    public AssertableInertiaPage HasPreserveFragment()
    {
        if (_page.PreserveFragment != true)
            throw new AssertionException("Expected preserveFragment to be true.");
        return this;
    }

    /// <summary>Asserts flash data exists with optional value check.</summary>
    public AssertableInertiaPage HasFlash(string key, object? expectedValue = null)
    {
        if (_page.Flash is null || !_page.Flash.TryGetValue(key, out var actualValue))
            throw new AssertionException($"Flash data '{key}' not found.");

        if (expectedValue is not null && !JsonSerializer.Serialize(actualValue).Contains(JsonSerializer.Serialize(expectedValue)))
            throw new AssertionException($"Flash '{key}' expected value '{expectedValue}', got '{actualValue}'.");
        return this;
    }

    /// <summary>Returns the underlying <see cref="InertiaPage"/>.</summary>
    public InertiaPage Page => _page;
}

/// <summary>Thrown when an assertion fails.</summary>
public sealed class AssertionException : Exception
{
    public AssertionException(string message) : base(message) { }
}
