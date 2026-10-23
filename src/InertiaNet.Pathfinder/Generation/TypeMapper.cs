namespace InertiaNet.Pathfinder.Generation;

static class TypeMapper
{
    private static readonly HashSet<string> NumberTypes = new()
    {
        "int", "long", "short", "byte", "float", "double", "decimal",
        "Int32", "Int64", "Int16", "Byte", "Single", "Double", "Decimal",
    };

    private static readonly HashSet<string> StringTypes = new()
    {
        "string", "String", "Guid",
        "DateTime", "DateTimeOffset", "DateOnly", "TimeOnly", "TimeSpan", "Uri",
    };

    private static readonly AsyncLocal<Dictionary<string, string>?> EnumState = new();
    private static Dictionary<string, string> KnownEnums => EnumState.Value ??= new(StringComparer.Ordinal);

    public static void RegisterEnum(string typeName, string tsType)
    {
        KnownEnums[typeName] = tsType;
    }

    public static void ClearEnums()
    {
        EnumState.Value = new(StringComparer.Ordinal);
    }

    public static bool IsEnum(string name) => name is not ("number" or "string") && KnownEnums.Values.Contains(name);

    public static string ToTypeScript(string clrType)
    {
        if (NumberTypes.Contains(clrType))
            return "number";

        if (StringTypes.Contains(clrType))
            return "string";

        if (clrType is "bool" or "Boolean")
            return "boolean";

        if (clrType == "object")
            return "unknown";

        if (KnownEnums.TryGetValue(clrType, out var enumType))
            return enumType;

        return "string | number";
    }
}
