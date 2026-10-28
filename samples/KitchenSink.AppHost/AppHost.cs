var builder = DistributedApplication.CreateBuilder(args);
var clientPath = Path.GetFullPath("../KitchenSink/ClientApp", builder.AppHostDirectory);

var vite = builder.AddViteApp("vite", clientPath)
    .WithNpm()
    .WithHttpHealthCheck("/@vite/client")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

var ssr = builder.AddJavaScriptApp("ssr", clientPath, "ssr:dev")
    .WithNpm()
    .WithHttpEndpoint(env: "SSR_PORT")
    .WithHttpHealthCheck("/health");

vite.WithEnvironment("SSR_URL", ssr.GetEndpoint("http"));

// SQLite is initialized and seeded by the web app. No container runtime is needed.
builder.AddProject<Projects.KitchenSink>("web")
    .WithHttpEndpoint(port: 5270, name: "http")
    .WithExternalHttpEndpoints()
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("SSR_ENABLED", "true")
    .WithEnvironment("SSR_URL", ssr.GetEndpoint("http"))
    .WaitFor(vite)
    .WaitFor(ssr);

builder.Build().Run();
