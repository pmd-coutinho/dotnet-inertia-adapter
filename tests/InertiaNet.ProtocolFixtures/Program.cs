using System.Net;
using System.Numerics;
using System.Text;
using System.Text.Json;
using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Ssr;
using InertiaNet.TagHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var results = new Dictionary<string, string>();
foreach (var policy in Enum.GetValues<LargeIntegerSerialization>())
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddInertiaWithSsr(options => { options.LargeIntegerSerialization = policy; options.Ssr.Enabled = false; });
    services.AddSingleton<IActionResultExecutor<ViewResult>, FixtureViewExecutor>();
    var handler = new CaptureSsr();
    services.AddHttpClient(InertiaSsr.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
    using var provider = services.BuildServiceProvider();
    using var scope = provider.CreateScope();
    var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
    context.Response.Body = new MemoryStream();
    context.Request.Headers["X-Inertia"] = "true";
    var inertia = scope.ServiceProvider.GetRequiredService<IInertiaService>();
    inertia.Flash("receipt", ulong.MaxValue);
    var props = new Dictionary<string, object?>
    {
        ["id"] = long.MaxValue, ["min"] = long.MinValue,
        ["huge"] = BigInteger.Pow(10, 80), ["small"] = 42L,
        ["nested"] = new { Safe = 9007199254740991L, Unsafe = 9007199254740992L, Values = new long?[] { long.MinValue, null } },
        ["lookup"] = new Dictionary<long, ulong> { [long.MaxValue] = ulong.MaxValue },
        ["text"] = "</script><p>untrusted</p>", ["decimal"] = 12.5m,
    };
    await inertia.Render("Numbers", props).ExecuteAsync(context);
    results[policy.ToString()] = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
    context.Response.Body = new MemoryStream();
    context.Request.Headers.Remove("X-Inertia");
    await inertia.Render("Numbers", props).ExecuteAsync(context);
    results[policy + "Html"] = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
    scope.ServiceProvider.GetRequiredService<IOptions<InertiaOptions>>().Value.Ssr.Enabled = true;
    var view = (FixtureViewExecutor)scope.ServiceProvider.GetRequiredService<IActionResultExecutor<ViewResult>>();
    await scope.ServiceProvider.GetRequiredService<ISsrGateway>().DispatchAsync(view.Page!);
    results[policy + "Ssr"] = handler.Body ?? throw new InvalidOperationException("SSR did not receive the page.");
}
await File.WriteAllTextAsync(args.Single(), JsonSerializer.Serialize(results));

sealed class FixtureViewExecutor(IOptions<InertiaOptions> options) : IActionResultExecutor<ViewResult>
{
    public InertiaPage? Page { get; private set; }
    public async Task ExecuteAsync(ActionContext context, ViewResult result)
    {
        Page = (InertiaPage)result.ViewData!.Model!;
        var helper = new InertiaTagHelper(new HttpContextAccessor { HttpContext = context.HttpContext }, options) { Id = "custom-root" };
        var output = new TagHelperOutput("inertia", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        helper.Process(new TagHelperContext([], new Dictionary<object, object>(), "numbers"), output);
        await context.HttpContext.Response.WriteAsync(output.Content.GetContent());
    }
}

sealed class CaptureSsr : HttpMessageHandler
{
    public string? Body { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Body = await request.Content!.ReadAsStringAsync(cancellationToken);
        return new(HttpStatusCode.OK) { Content = new StringContent("""{"head":[],"body":"<div id=\"custom-root\"></div>"}""", Encoding.UTF8, "application/json") };
    }
}
