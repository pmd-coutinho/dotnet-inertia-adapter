using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace InertiaNet.Diagnostics;

/// <summary>An explicitly exported snapshot of the application's registered HTTP endpoints.</summary>
public sealed record InertiaEndpointManifest(int SchemaVersion, IReadOnlyList<InertiaEndpointDescription> Endpoints)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Enumerates endpoint metadata without executing request handlers or props factories.</summary>
    public static InertiaEndpointManifest Capture(IEndpointRouteBuilder application)
        => Capture(application.DataSources);

    /// <summary>Enumerates route endpoints from explicitly supplied data sources.</summary>
    public static InertiaEndpointManifest Capture(IEnumerable<EndpointDataSource> sources)
    {
        var endpoints = sources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().Select(endpoint =>
        {
            var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            return new InertiaEndpointDescription(
                endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? endpoint.Metadata.GetMetadata<IRouteNameMetadata>()?.RouteName,
                endpoint.RoutePattern.RawText ?? PatternText(endpoint.RoutePattern),
                methods is { Count: > 0 } ? methods.Select(method => method.ToUpperInvariant()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() : ["*"],
                endpoint.Metadata.GetMetadata<IHostMetadata>()?.Hosts.Order(StringComparer.OrdinalIgnoreCase).ToArray() ?? [],
                endpoint.Metadata.GetMetadata<InertiaComponentMetadata>()?.Component,
                action?.ControllerTypeInfo.FullName, action?.MethodInfo.Name,
                endpoint.Order,
                endpoint.Metadata.GetMetadata<ISuppressLinkGenerationMetadata>()?.SuppressLinkGeneration ?? false,
                endpoint.Metadata.GetMetadata<ISuppressMatchingMetadata>()?.SuppressMatching ?? false);
        }).OrderBy(endpoint => endpoint.Name, StringComparer.Ordinal).ThenBy(endpoint => endpoint.Pattern, StringComparer.Ordinal)
            .ThenBy(endpoint => string.Join(',', endpoint.Methods), StringComparer.Ordinal).ToArray();
        return new(1, endpoints);
    }

    /// <summary>Writes a local JSON snapshot atomically. Export is opt-in and never maps a public endpoint.</summary>
    public async Task WriteAsync(string path, CancellationToken cancellationToken = default)
    {
        var target = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(this, JsonOptions) + "\n", cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string PatternText(RoutePattern pattern)
        => "/" + string.Join('/', pattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part => part switch
        {
            RoutePatternLiteralPart literal => literal.Content.Replace("{", "{{").Replace("}", "}}"),
            RoutePatternSeparatorPart separator => separator.Content,
            RoutePatternParameterPart parameter => "{" + (parameter.IsCatchAll ? parameter.EncodeSlashes ? "*" : "**" : "") + parameter.Name
                + string.Concat(parameter.ParameterPolicies.Where(policy => policy.Content is not null).Select(policy => ":" + policy.Content))
                + (parameter.Default is null ? "" : "=" + Convert.ToString(parameter.Default, CultureInfo.InvariantCulture))
                + (parameter.IsOptional ? "?" : "") + "}",
            _ => throw new InvalidOperationException("Unsupported route-pattern part."),
        }))));
}

/// <summary>Wire metadata for a registered route. Methods containing '*' accept every method.</summary>
public sealed record InertiaEndpointDescription(
    string? Name, string Pattern, string[] Methods, string[] Hosts, string? Component,
    string? Controller, string? Action, int Order, bool SuppressLinkGeneration, bool SuppressMatching);

/// <summary>Declares a static Inertia component associated with an endpoint.</summary>
public sealed record InertiaComponentMetadata(string Component);
