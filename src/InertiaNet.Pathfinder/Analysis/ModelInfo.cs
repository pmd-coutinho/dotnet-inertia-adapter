namespace InertiaNet.Pathfinder.Analysis;

record ModelInfo(
    string FullName,
    string ShortName,
    List<ModelProperty> Properties,
    bool HasExtensionData = false,
    string Usage = "output");

record ModelProperty(
    string Name,
    string TypeScriptType,
    bool IsNullable,
    bool IsCollection,
    bool? IsOptional = null,
    IReadOnlyDictionary<string, object?>? Validation = null);
