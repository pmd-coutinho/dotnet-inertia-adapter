using System.Text;
using System.Text.Json;
using InertiaNet.Pathfinder.Analysis;

namespace InertiaNet.Pathfinder.Generation;

/// <summary>One emitter for the named-route and controller-action contracts.</summary>
internal static class RouteFunctionWriter
{
    public static void Write(StringBuilder sb, RouteInfo route, string name, string payloadName,
        bool forms, List<ModelInfo>? models)
    {
        if (route.BodyTypeName is not null)
            sb.AppendLine($"export type {payloadName}Payload = {route.BodyTypeScriptType ?? BodyType(route.BodyTypeName, models)}\n");
        if (route.QueryTypeScriptType is not null) sb.AppendLine($"export type {payloadName}Query = {route.QueryTypeScriptType}\n");
        if (route.FormTypeScriptType is not null) sb.AppendLine($"export type {payloadName}Form = {route.FormTypeScriptType}\n");
        var parameters = AllParameters(route);
        var hasParams = parameters.Length > 0;
        var args = hasParams ? $"args{(parameters.All(p => p.IsOptional) ? "?" : "")}: {ArgsType(parameters)}, " : "";
        var signature = args + "options?: RouteQueryOptions";
        var call = hasParams ? "args, options" : "options";
        var method = route.HttpMethods[0];
        sb.AppendLine("/**");
        if (route.SourceFile is not null && route.SourceLine is not null)
        {
            sb.AppendLine($" * @see {route.ControllerFullName}::{route.ActionName}");
            sb.AppendLine($" * @see {Comment(route.SourceFile)}:{route.SourceLine}");
        }
        sb.AppendLine($" * @route {Comment(route.UrlTemplate)}");
        sb.AppendLine(" */");
        sb.AppendLine($"export const {name} = ({signature}): RouteDefinition<{Json(method)}> => ({{");
        sb.AppendLine($"    url: {name}.url({call}), method: {Json(method)},");
        sb.AppendLine("})");
        var methods = Json(route.HttpMethods);
        sb.AppendLine($"{name}.definition = {{ methods: {methods}, url: {Json(DefinitionUrl(route))} }} satisfies RouteDefinitionInfo<{methods}>");
        if (!hasParams)
            sb.AppendLine($"{name}.url = ({signature}) => {name}.definition.url + queryParams(options)");
        else
        {
            sb.AppendLine($"{name}.url = ({signature}) => {{");
            sb.AppendLine($"    const parameters = applyUrlDefaults(normalizeParameters(args, {Json(parameters.Select(p => p.Name).ToArray())}))");
            sb.AppendLine($"    return buildUrl({Json(route.RouteName ?? name)}, {name}.definition.url, parameters, {ParameterDefinitions(route, parameters)}) + queryParams(options)");
            sb.AppendLine("}");
        }
        foreach (var verb in route.HttpMethods)
            sb.AppendLine($"{name}.{verb} = ({signature}): RouteDefinition<{Json(verb)}> => ({{ url: {name}.url({call}), method: {Json(verb)} }})");
        if (forms)
        {
            sb.AppendLine($"{name}.form = Object.assign(({signature}): FormDefinition<{Json(FormMethod(method))}> => ({{");
            FormBody(sb, name, call, method);
            sb.AppendLine("}), {");
            foreach (var verb in route.HttpMethods)
            {
                sb.AppendLine($"    {verb}: ({signature}): FormDefinition<{Json(FormMethod(verb))}> => ({{");
                FormBody(sb, name, call, verb);
                sb.AppendLine("    }),");
            }
            sb.AppendLine("})");
        }
        if (route.BodyTypeName is not null)
            sb.AppendLine($"{name}.body = undefined as unknown as {route.BodyTypeScriptType ?? BodyType(route.BodyTypeName, models)}");
        if (route.QueryTypeScriptType is not null) sb.AppendLine($"{name}.query = undefined as unknown as {route.QueryTypeScriptType}");
        if (route.FormTypeScriptType is not null) sb.AppendLine($"{name}.formData = undefined as unknown as {route.FormTypeScriptType}");
        sb.AppendLine();
    }

    private static void FormBody(StringBuilder sb, string name, string call, string method)
    {
        var safe = FormMethod(method);
        var data = safe == method ? "" : $", data: {{ _method: {Json(method.ToUpperInvariant())} }}";
        sb.AppendLine($"    action: {name}.url({call}), method: {Json(safe)}{data},");
    }

    private static string Comment(string value) => value.Replace("*/", "* /", StringComparison.Ordinal).Replace('\n', ' ').Replace('\r', ' ');
    private static string FormMethod(string method) => method == "get" ? "get" : "post";
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
    private static string DefinitionUrl(RouteInfo route) => route.Domain is null ? route.UrlTemplate
        : (route.Scheme is null ? "//" : route.Scheme + "://") + route.Domain + route.UrlTemplate;

    public static string BodyType(string name, List<ModelInfo>? models)
    {
        var mapped = TypeMapper.ToTypeScript(name);
        return mapped != "string | number" ? mapped : models?.Any(model => model.ShortName == name) == true ? name : "unknown";
    }

    public static IEnumerable<string> ModelImports(IEnumerable<RouteInfo> routes, List<ModelInfo>? models)
        => routes.SelectMany(route => new[] { route.BodyTypeScriptType ?? route.BodyTypeName, route.QueryTypeScriptType, route.FormTypeScriptType }).OfType<string>()
            .SelectMany(TypeReferenceCollector.Collect)
            .Where(name => models?.Any(model => model.ShortName == name) == true)
            .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal);

    public static IEnumerable<string> EnumImports(IEnumerable<RouteInfo> routes)
        => routes.SelectMany(route => route.Parameters.Select(parameter => parameter.TypeScriptType ?? TypeMapper.ToTypeScript(parameter.ClrTypeName))
                .Concat(new[] { route.BodyTypeScriptType ?? (route.BodyTypeName is null ? null : TypeMapper.ToTypeScript(route.BodyTypeName)), route.QueryTypeScriptType, route.FormTypeScriptType }.OfType<string>()))
            .SelectMany(TypeReferenceCollector.Collect).Where(TypeMapper.IsEnum)
            .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal);

    private static RouteParameter[] AllParameters(RouteInfo route)
    {
        var hosts = route.Domain is null ? [] : System.Text.RegularExpressions.Regex.Matches(route.Domain, @"\{(\w+)\}")
            .Select(match => match.Groups[1].Value).Where(name => !route.Parameters.Any(p => p.Name == name))
            .Select(name => new RouteParameter(name, "string", false)).ToArray();
        return [..hosts, ..route.Parameters];
    }

    private static string ArgsType(RouteParameter[] parameters)
    {
        string Type(RouteParameter parameter) => parameter.TypeScriptType ?? TypeMapper.ToTypeScript(parameter.ClrTypeName);
        var fields = string.Join("; ", parameters.Select(parameter => $"{Json(parameter.Name)}{(parameter.IsOptional ? "?" : "")}: {Type(parameter)}"));
        var tuple = string.Join(", ", parameters.Select((parameter, index) =>
        {
            var optional = parameter.IsOptional && parameters.Skip(index + 1).All(p => p.IsOptional);
            return $"{NameUtils.SafeJsName(parameter.Name)}{(optional ? "?" : "")}: {Type(parameter)}{(parameter.IsOptional && !optional ? " | undefined" : "")}";
        }));
        return $"{{ {fields} }} | [{tuple}]" + (parameters.Length == 1 ? $" | {Type(parameters[0])}" : "");
    }

    private static string ParameterDefinitions(RouteInfo route, RouteParameter[] parameters)
    {
        var definitions = parameters.Select(parameter =>
        {
            var definition = new Dictionary<string, object?>
            {
                ["name"] = parameter.Name, ["optional"] = parameter.IsOptional,
                ["placeholder"] = RouteTemplateParser.Placeholder(parameter.Name, parameter.IsOptional, parameter.CatchAll),
            };
            if (parameter.DefaultValue is not null) definition["defaultValue"] = parameter.DefaultValue;
            if (parameter.CatchAll == 2) definition["preserveSlashes"] = true;
            if (route.Domain?.Contains("{" + parameter.Name + "}", StringComparison.Ordinal) == true)
                definition["host"] = true;
            return definition;
        });
        return Json(definitions);
    }
}
