namespace InertiaNet.Pathfinder.Analysis;

record EnumInfo(
    string FullName,
    string ShortName,
    List<EnumMember> Members,
    bool StringValues = false,
    string? WireType = null);

record EnumMember(
    string Name,
    string? Value,
    bool BigInt = false);
