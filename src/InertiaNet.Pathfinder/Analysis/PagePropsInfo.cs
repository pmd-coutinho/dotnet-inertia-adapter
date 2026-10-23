namespace InertiaNet.Pathfinder.Analysis;

record PagePropsInfo(
    string ComponentName,
    List<PropField> Props,
    bool HasExtensionData = false,
    string? TypeName = null,
    string? FileName = null);

record PropField(
    string Name,
    string TypeScriptType,
    bool IsOptional,
    bool IsDeferred,
    bool IsLiteral = false);
