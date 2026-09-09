using System.Text;
using System.Text.Json;
using PrintableBook.UpdateSecurity;

namespace PrintableBook.UpdateSecurity.Tests;

public sealed class UpdateManifestCodecTests
{
    [Fact]
    public void SerializeIsDeterministicCompactUtf8InContractOrder()
    {
        var first = UpdateManifestCodec.Serialize(ValidManifest());
        var second = UpdateManifestCodec.Serialize(ValidManifest());

        Assert.Equal(first, second);
        Assert.Equal((byte)'{', first[0]);
        Assert.DoesNotContain((byte)'\n', first);
        Assert.DoesNotContain((byte)'\r', first);
        Assert.False(first.Length >= 3 && first[0] == 0xef && first[1] == 0xbb && first[2] == 0xbf);

        var json = Encoding.UTF8.GetString(first);
        AssertOrdered(json, "schemaVersion", "product", "version", "runtimeIdentifier", "archive", "checksum");
    }

    [Fact]
    public void DeserializeRejectsUnknownProperties()
    {
        var json = Encoding.UTF8.GetString(UpdateManifestCodec.Serialize(ValidManifest()));
        var bytes = Encoding.UTF8.GetBytes(json.Insert(1, "\"unexpected\":\"value\","));

        Assert.Throws<JsonException>(() => UpdateManifestCodec.Deserialize(bytes));
    }

    [Fact]
    public void DeserializeRejectsValidJsonThatViolatesTheContract()
    {
        var json = Encoding.UTF8.GetString(UpdateManifestCodec.Serialize(ValidManifest()));
        var bytes = Encoding.UTF8.GetBytes(json.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal));

        Assert.Throws<InvalidDataException>(() => UpdateManifestCodec.Deserialize(bytes));
    }

    private static UpdateReleaseManifest ValidManifest() => new(
        1,
        "PrintableBook",
        "0.2.0",
        "win-x64",
        new UpdateReleaseAsset("PrintableBook-0.2.0-win-x64.zip", 42, new string('a', 64)),
        new UpdateReleaseAsset("PrintableBook-0.2.0-win-x64.zip.sha256", 21, new string('b', 64)));

    private static void AssertOrdered(string json, params string[] properties)
    {
        var previous = -1;
        foreach (var property in properties)
        {
            var position = json.IndexOf($"\"{property}\"", StringComparison.Ordinal);
            Assert.True(position > previous, $"Expected {property} after the previous manifest property.");
            previous = position;
        }
    }
}
