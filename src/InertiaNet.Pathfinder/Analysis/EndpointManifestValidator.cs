using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace InertiaNet.Pathfinder.Analysis;

internal static class EndpointManifestValidator
{
    private sealed record Manifest(int SchemaVersion, Endpoint[] Endpoints);
    private sealed record Endpoint(string? Name, string Pattern, string[] Methods, string[] Hosts, string? Component,
        string? Controller, string? Action, int Order, bool SuppressLinkGeneration, bool SuppressMatching);

    public static void Validate(List<RouteInfo> routes, string path, PathfinderConfig config)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw new ArgumentException("Empty endpoint manifest.");
        if (manifest.SchemaVersion != 1 || manifest.Endpoints is null)
            throw new ArgumentException("Unsupported endpoint manifest. Expected schemaVersion 1 and an endpoints array.");
        if (manifest.Endpoints.Any(endpoint => endpoint is null || string.IsNullOrWhiteSpace(endpoint.Pattern)
                || endpoint.Methods is null || endpoint.Hosts is null || endpoint.Methods.Length == 0
                || endpoint.Methods.Any(string.IsNullOrWhiteSpace) || endpoint.Hosts.Any(string.IsNullOrWhiteSpace)))
            throw new ArgumentException("Malformed endpoint manifest entry.");
        var endpoints = manifest.Endpoints.Where(endpoint => !endpoint.SuppressMatching && !config.SkipPatterns.Any(pattern =>
            Regex.IsMatch(endpoint.Controller ?? "__MinimalApi__", "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))).ToArray();
        var normalized = endpoints.ToDictionary(endpoint => endpoint, endpoint => Normalize(endpoint.Pattern));
        foreach (var route in routes)
        {
            var candidates = endpoints.Where(endpoint => route.RouteName is not null
                ? endpoint.Name == route.RouteName : normalized[endpoint] == route.UrlTemplate).ToArray();
            if (candidates.Length == 0)
            {
                Report(route, $"Source endpoint '{route.RouteName ?? route.ActionName}' ({route.UrlTemplate}) is absent from the registered endpoint manifest.", "PATHFINDER400");
                continue;
            }
            if (!candidates.Any(endpoint => normalized[endpoint] == route.UrlTemplate && MethodsMatch(route, endpoint)
                    && HostsMatch(route, endpoint) && (route.ComponentName is null || endpoint.Component is null || route.ComponentName == endpoint.Component)
                    && DefaultsMatch(route, endpoint)))
            {
                var registered = string.Join("; ", candidates.Select(endpoint => $"{string.Join(',', endpoint.Methods)} {endpoint.Pattern} hosts=[{string.Join(',', endpoint.Hosts)}] component={endpoint.Component ?? "unspecified"}"));
                Report(route, $"Source endpoint '{route.RouteName ?? route.ActionName}' disagrees with registered metadata. Source: {string.Join(',', route.HttpMethods)} {route.UrlTemplate}; registered: {registered}.", "PATHFINDER401");
            }
        }
        // Named endpoints are the durable public contract. Unnamed framework
        // infrastructure endpoints do not require a generated helper.
        foreach (var endpoint in endpoints.Where(endpoint => endpoint.Name is not null).DistinctBy(endpoint => endpoint.Name))
            if (!routes.Any(route => route.RouteName == endpoint.Name))
                PathfinderDiagnostics.Report(path, 1,
                    $"Registered named endpoint '{endpoint.Name}' ({endpoint.Pattern}) was not discovered in source.", "PATHFINDER402");
    }

    private static bool MethodsMatch(RouteInfo route, Endpoint endpoint)
        => route.HttpMethods.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(endpoint.Methods);

    private static bool HostsMatch(RouteInfo route, Endpoint endpoint)
        => route.Domain is null ? endpoint.Hosts.Length == 0 : endpoint.Hosts.Length == 1
            && route.Domain.Equals(endpoint.Hosts[0], StringComparison.OrdinalIgnoreCase);

    private static bool DefaultsMatch(RouteInfo route, Endpoint endpoint)
        => RouteTemplateParser.Parse(endpoint.Pattern).All(parameter => parameter.DefaultValue is null
            || route.Parameters.Any(source => source.Name == parameter.Name && source.DefaultValue == parameter.DefaultValue));

    private static string Normalize(string pattern)
    {
        try { return RouteTemplateParser.StripConstraints(pattern); }
        catch (Microsoft.AspNetCore.Routing.Patterns.RoutePatternException exception)
        { throw new ArgumentException($"Invalid route pattern in endpoint manifest: {pattern}", exception); }
    }

    private static void Report(RouteInfo route, string message, string code)
        => PathfinderDiagnostics.Report(route.SourceFile ?? "endpoints", route.SourceLine ?? 1, message, code);
}
