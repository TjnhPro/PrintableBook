using System.Text.Json.Serialization;

namespace PrintableBook.UpdateSecurity;

public sealed record UpdateReleaseManifest(
    [property: JsonPropertyName("schemaVersion"), JsonPropertyOrder(0)] int SchemaVersion,
    [property: JsonPropertyName("product"), JsonPropertyOrder(1)] string Product,
    [property: JsonPropertyName("version"), JsonPropertyOrder(2)] string Version,
    [property: JsonPropertyName("runtimeIdentifier"), JsonPropertyOrder(3)] string RuntimeIdentifier,
    [property: JsonPropertyName("archive"), JsonPropertyOrder(4)] UpdateReleaseAsset Archive,
    [property: JsonPropertyName("checksum"), JsonPropertyOrder(5)] UpdateReleaseAsset Checksum);
