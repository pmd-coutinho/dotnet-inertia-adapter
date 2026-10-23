using InertiaNet.Core;
using InertiaNet.Diagnostics;
using InertiaNet.Extensions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Playground;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddAntiforgery(options => options.HeaderName = "X-XSRF-TOKEN");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
builder.Services.AddAuthorization();
builder.Services.AddSingleton<TodoRepository>();
builder.Services.AddInertiaWithSsr<PlaygroundMiddleware>(options =>
{
    options.RootView = "/Views/App.cshtml";
    options.Version = () => "playground-v1";
    options.LargeIntegerSerialization = LargeIntegerSerialization.String;
    options.Ssr.Enabled = builder.Configuration.GetValue("SSR_ENABLED", true);
    options.Ssr.Url = builder.Configuration["SSR_URL"] ?? "http://127.0.0.1:13714";
    options.Ssr.Timeout = TimeSpan.FromSeconds(1);
    options.PropDiagnostics.Enabled = builder.Environment.IsDevelopment();
});
builder.Services.AddViteHelper(options => { options.PublicDirectory = "wwwroot"; options.BuildDirectory = "build"; });

var app = builder.Build();
app.UseStaticFiles();
app.UseHttpMethodOverride(new HttpMethodOverrideOptions { FormFieldName = "_method" });
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method))
    {
        var tokens = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
        context.Items["Playground.CsrfToken"] = tokens.RequestToken;
        context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false, SameSite = SameSiteMode.Lax, Secure = context.Request.IsHttps, Path = "/",
        });
    }
    await next(context);
});
app.UseInertia<PlaygroundMiddleware>();
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/react/todos"));
if (Environment.GetEnvironmentVariable("PATHFINDER_EXPORT_ENDPOINTS") is { } manifest)
{
    await InertiaEndpointManifest.Capture(app).WriteAsync(manifest);
    return;
}
app.Run();
