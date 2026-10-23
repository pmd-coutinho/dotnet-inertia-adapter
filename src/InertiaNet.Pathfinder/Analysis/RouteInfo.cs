namespace InertiaNet.Pathfinder.Analysis;

record RouteInfo(
    string ControllerFullName,
    string ControllerShortName,
    string ActionName,
    string[] HttpMethods,
    string UrlTemplate,
    RouteParameter[] Parameters,
    string? RouteName,
    string? SourceFile = null,
    int? SourceLine = null,
    string? BodyTypeName = null,
    string? Domain = null,
    string? Scheme = null,
    string? ComponentName = null,
    string? BodyTypeScriptType = null,
    string? QueryTypeScriptType = null,
    string? FormTypeScriptType = null,
    int? SourceSpanStart = null);
