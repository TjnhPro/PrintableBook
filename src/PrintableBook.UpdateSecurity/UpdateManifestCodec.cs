using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrintableBook.UpdateSecurity;

public static class UpdateManifestCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static byte[] Serialize(UpdateReleaseManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        UpdateManifestContract.Validate(manifest);
        return JsonSerializer.SerializeToUtf8Bytes(manifest, Options);
    }

    public static UpdateReleaseManifest Deserialize(ReadOnlySpan<byte> bytes)
    {
        var manifest = JsonSerializer.Deserialize<UpdateReleaseManifest>(bytes, Options) ?? throw new InvalidDataException("Update manifest is empty.");
        UpdateManifestContract.Validate(manifest);
        return manifest;
    }
}
