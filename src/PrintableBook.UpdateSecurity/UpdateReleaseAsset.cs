using System.Text.Json.Serialization;

namespace PrintableBook.UpdateSecurity;

public sealed record UpdateReleaseAsset(
    [property: JsonPropertyName("name"), JsonPropertyOrder(0)] string Name,
    [property: JsonPropertyName("sizeBytes"), JsonPropertyOrder(1)] long SizeBytes,
    [property: JsonPropertyName("sha256"), JsonPropertyOrder(2)] string Sha256);
