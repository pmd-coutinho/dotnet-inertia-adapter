using System.Text.Json.Serialization;

namespace InertiaNet.Resolution;

// Metadata names belong to the protocol, not the application's JSON naming policy.
internal sealed record OnceMetadata(
    [property: JsonPropertyName("prop")] string Prop,
    [property: JsonPropertyName("expiresAt"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? ExpiresAt);

internal sealed record ScrollMetadataValue(
    [property: JsonPropertyName("pageName"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? PageName,
    [property: JsonPropertyName("previousPage"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] object? PreviousPage,
    [property: JsonPropertyName("nextPage"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] object? NextPage,
    [property: JsonPropertyName("currentPage"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] object? CurrentPage,
    [property: JsonPropertyName("reset"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] bool Reset);
