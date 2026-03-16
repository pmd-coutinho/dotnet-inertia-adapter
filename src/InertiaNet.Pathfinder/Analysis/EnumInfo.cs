namespace InertiaNet.Pathfinder.Analysis;

record EnumInfo(
    string FullName,
    string ShortName,
    List<EnumMember> Members);

record EnumMember(
    string Name,
    string? Value);
