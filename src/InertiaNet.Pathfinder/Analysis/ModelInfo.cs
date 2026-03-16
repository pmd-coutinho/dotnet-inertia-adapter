namespace InertiaNet.Pathfinder.Analysis;

record ModelInfo(
    string FullName,
    string ShortName,
    List<ModelProperty> Properties);

record ModelProperty(
    string Name,
    string TypeScriptType,
    bool IsNullable,
    bool IsCollection);
