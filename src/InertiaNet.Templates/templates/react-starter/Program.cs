using InertiaNet.Extensions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddAntiforgery(options => options.HeaderName = "X-XSRF-TOKEN");
builder.Services.AddInertia(options =>
{
    options.RootView = "/Views/App.cshtml";
    options.Pages.EnsurePagesExist = true;
    options.Pages.Paths = ["ClientApp/src/pages"];
});
builder.Services.AddViteHelper(options =>
{
    options.PublicDirectory = "wwwroot";
    options.BuildDirectory = "build";
    options.ManifestFilename = "manifest.json";
    options.HotFile = "hot";
});

var app = builder.Build();

app.UseStaticFiles();
app.UseHttpMethodOverride(new HttpMethodOverrideOptions { FormFieldName = "_method" });
app.UseRouting();
// Match Inertia's readable-cookie/header convention for browser submissions.
app.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method))
    {
        var tokens = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
        context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false, SameSite = SameSiteMode.Lax, Secure = context.Request.IsHttps, Path = "/",
        });
    }
    await next(context);
});
app.UseAntiforgery();
app.UseInertia();

app.MapInertia("/", "Home");
app.MapInertiaFallback("Home");

app.Run();
