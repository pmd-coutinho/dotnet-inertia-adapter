using InertiaNet.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddInertia(options =>
{
    options.RootView = "App";
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
app.UseRouting();
app.UseInertia();

app.MapInertia("/", "Home");
app.MapInertiaFallback("Home");

app.Run();
