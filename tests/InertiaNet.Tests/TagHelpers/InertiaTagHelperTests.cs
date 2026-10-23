using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Ssr;
using InertiaNet.TagHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace InertiaNet.Tests.TagHelpers;

public class InertiaTagHelperTests
{
    [Theory]
    [InlineData("</script><div id=probe>marker</div>")]
    [InlineData("</ScRiPt><script>marker</script>")]
    [InlineData("<!-- <script> & < > Olá 日本語 \u2028\u2029")]
    public void InitialJson_RemainsData_EvenWithRawConverters(string text)
    {
        var options = new InertiaOptions { JsonSerializerOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping } };
        options.JsonSerializerOptions.Converters.Add(new RawTextConverter());
        var page = new InertiaPage { Component = "Home", Url = "/", Props = new() { ["text"] = new RawText(text) } };
        var html = Render(page, options: options);
        var start = html.IndexOf('>', html.IndexOf("<script", StringComparison.Ordinal)) + 1;
        var end = html.IndexOf("</script>", start, StringComparison.OrdinalIgnoreCase);
        var json = html[start..end];
        json.Should().NotContain("<").And.NotContain(">").And.NotContain("&");
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("props").GetProperty("text").GetString().Should().Be(text);
        html[end..].Should().Be("</script>");
    }

    [Theory]
    [InlineData("app")]
    [InlineData("custom-root")]
    public void SsrBody_IsEmittedExactlyOnce_WithItsOwnRootAndScript(string id)
    {
        var body = $"<script data-page=\"{id}\" type=\"application/json\">{{}}</script><div id=\"{id}\" data-server-rendered=\"true\"><p>SSR</p></div>";
        Render(new InertiaPage { Component = "Home", Url = "/", Props = [] }, new("", body), id: id)
            .Should().Be(body);
    }

    [Fact]
    public void CsrFallback_EncodesCustomRootAttributes()
    {
        var html = Render(new InertiaPage { Component = "Home", Url = "/", Props = [] }, id: "custom\"root");
        html.Should().Contain("id=\"custom&quot;root\"").And.Contain("data-page=\"custom&quot;root\"");
    }

    private static string Render(InertiaPage page, SsrResponse? ssr = null, InertiaOptions? options = null, string id = "app")
    {
        var http = new DefaultHttpContext();
        http.Items[InertiaContextKeys.PageKey] = page;
        http.Items[InertiaContextKeys.SsrResponseKey] = ssr;
        var helper = new InertiaTagHelper(new HttpContextAccessor { HttpContext = http }, Options.Create(options ?? new())) { Id = id };
        var output = new TagHelperOutput("inertia", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        helper.Process(new TagHelperContext([], new Dictionary<object, object>(), "test"), output);
        return output.Content.GetContent();
    }

    private sealed record RawText(string Text);
    private sealed class RawTextConverter : JsonConverter<RawText>
    {
        public override RawText Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, RawText value, JsonSerializerOptions options)
            => writer.WriteRawValue(JsonSerializer.Serialize(value.Text, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
}
