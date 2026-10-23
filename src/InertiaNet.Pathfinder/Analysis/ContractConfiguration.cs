using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace InertiaNet.Pathfinder.Analysis;

/// <summary>Describes wire contracts without running application configuration code.</summary>
internal sealed class ContractConfiguration
{
    public SerializationProfile Output { get; init; } = new();
    public SerializationProfile Input { get; init; } = new()
    {
        IgnoreCondition = JsonIgnoreCondition.Never,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static ContractConfiguration Load(string path)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        var file = JsonSerializer.Deserialize<FileProfile>(File.ReadAllText(path), options)
            ?? throw new ArgumentException("The contract configuration must be an object.");
        var result = new ContractConfiguration();
        Apply(file.Output, result.Output);
        Apply(file.Input, result.Input);
        return result;
    }

    private static void Apply(Profile? source, SerializationProfile target)
    {
        if (source is null) return;
        if (source.Naming is not null) target.NamingPolicy = Naming(source.Naming);
        if (source.IgnoreCondition is { } ignore) target.IgnoreCondition = ignore;
        if (source.IncludeFields is { } fields) target.IncludeFields = fields;
        if (source.IgnoreReadOnlyProperties is { } properties) target.IgnoreReadOnlyProperties = properties;
        if (source.IgnoreReadOnlyFields is { } readOnlyFields) target.IgnoreReadOnlyFields = readOnlyFields;
        if (source.StringEnums is { } enums) target.StringEnums = enums;
        if (source.EnumNaming is not null) target.EnumNamingPolicy = Naming(source.EnumNaming);
        if (source.EnumAllowIntegers is { } integers) target.EnumAllowIntegers = integers;
        if (source.NumberHandling is { } numbers) target.NumberHandling = numbers;
        if (source.LargeIntegers is { } largeIntegers) target.LargeIntegers = largeIntegers;
        foreach (var (name, wireType) in source.TypeOverrides ?? [])
        {
            // Overrides are deliberately finite scalar unions, never executable TS or imports.
            if (string.IsNullOrWhiteSpace(name) || !Regex.IsMatch(wireType,
                    "^\\s*(?:string|number|boolean|bigint|null|unknown)(?:\\s*\\|\\s*(?:string|number|boolean|bigint|null|unknown))*\\s*$",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                throw new ArgumentException($"Invalid contract override '{name}'. Expected a scalar union such as 'string | null'.");
            target.TypeOverrides.Add(name.Replace("global::", "", StringComparison.Ordinal), wireType.Trim());
        }
    }

    private static JsonNamingPolicy? Naming(string value) => value switch
    {
        "preserve" => null, "camelCase" => JsonNamingPolicy.CamelCase,
        "snake_case" => JsonNamingPolicy.SnakeCaseLower, "SNAKE_CASE" => JsonNamingPolicy.SnakeCaseUpper,
        "kebab-case" => JsonNamingPolicy.KebabCaseLower, "KEBAB-CASE" => JsonNamingPolicy.KebabCaseUpper,
        _ => throw new ArgumentException($"Unknown JSON naming policy '{value}'."),
    };

    private sealed class FileProfile
    {
        public Profile? Output { get; set; }
        public Profile? Input { get; set; }
    }

    private sealed class Profile
    {
        public string? Naming { get; set; }
        public JsonIgnoreCondition? IgnoreCondition { get; set; }
        public bool? IncludeFields { get; set; }
        public bool? IgnoreReadOnlyProperties { get; set; }
        public bool? IgnoreReadOnlyFields { get; set; }
        public bool? StringEnums { get; set; }
        public string? EnumNaming { get; set; }
        public bool? EnumAllowIntegers { get; set; }
        public JsonNumberHandling? NumberHandling { get; set; }
        public LargeIntegerHandling? LargeIntegers { get; set; }
        public Dictionary<string, string>? TypeOverrides { get; set; }
    }
}
