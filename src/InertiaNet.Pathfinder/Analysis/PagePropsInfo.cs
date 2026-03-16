namespace InertiaNet.Pathfinder.Analysis;

record PagePropsInfo(
    string ComponentName,
    List<PropField> Props);

record PropField(
    string Name,
    string TypeScriptType,
    bool IsOptional,
    bool IsDeferred);
