using System.Text.Json;
using System.Text.Json.Serialization;

namespace InertiaNet.Pathfinder.Analysis;

/// <summary>The statically supported JSON contract, matching InertiaNet defaults.</summary>
internal sealed class SerializationProfile
{
    public JsonNamingPolicy? NamingPolicy { get; set; } = JsonNamingPolicy.CamelCase;
    public JsonIgnoreCondition IgnoreCondition { get; set; } = JsonIgnoreCondition.WhenWritingNull;
    public bool IncludeFields { get; set; }
    public bool IgnoreReadOnlyProperties { get; set; }
    public bool IgnoreReadOnlyFields { get; set; }
    public bool StringEnums { get; set; }
    public JsonNamingPolicy? EnumNamingPolicy { get; set; }
    public bool EnumAllowIntegers { get; set; } = true;
    public JsonNumberHandling NumberHandling { get; set; }
    public LargeIntegerHandling LargeIntegers { get; set; }
    public Dictionary<string, string> TypeOverrides { get; set; } = new(StringComparer.Ordinal);
    public string Name(string value) => NamingPolicy?.ConvertName(value) ?? value;
}

internal enum LargeIntegerHandling { Number, String, BigInt }
