using InertiaNet.Core;
using InertiaNet.Extensions;
using KitchenSink;
using KitchenSink.Controllers;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddAntiforgery(options => options.HeaderName = "X-XSRF-TOKEN");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
builder.Services.AddAuthorization();
builder.Services.AddSingleton<ContactRepository>();
builder.Services.AddInertiaWithSsr<KitchenSinkMiddleware>(options =>
{
    options.RootView = "/Views/App.cshtml";
    options.Version = () => "kitchen-sink-v1";
    options.Ssr.Enabled = builder.Configuration.GetValue("SSR_ENABLED", true);
    options.Ssr.Url = builder.Configuration["SSR_URL"] ?? "http://127.0.0.1:13715";
    options.Ssr.Timeout = TimeSpan.FromSeconds(1);
    options.PropDiagnostics.Enabled = builder.Environment.IsDevelopment();
    options.HandleExceptionsUsing = (exception, context) =>
    {
        if (exception is not DemoRenderException) return null;
        // The adapter's JSON writer sets 200. Apply the error status just before headers are sent.
        context.Response.OnStarting(() =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Task.CompletedTask;
        });
        return InertiaResults.Inertia("Error", new { message = "The demonstration error was handled by InertiaNet." });
    };
});
builder.Services.AddInertiaEventHandler<RenderObserver>();
builder.Services.AddViteHelper(options => { options.PublicDirectory = "wwwroot"; options.BuildDirectory = "build"; });

var app = builder.Build();
app.Services.GetRequiredService<ContactRepository>().Initialize();
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
        context.Items["KitchenSink.CsrfToken"] = tokens.RequestToken;
        context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false, SameSite = SameSiteMode.Lax, Secure = context.Request.IsHttps, Path = "/",
        });
    }
    await next(context);
});
app.UseInertia<KitchenSinkMiddleware>();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
app.MapGet("/minimal", (IInertiaService inertia) => inertia.Render("Minimal", new { message = "This page came from a Minimal API endpoint." }));
app.Run();
