using PrintableBook.UpdateSecurity;

namespace PrintableBook.UpdateSecurity.Tests;

public sealed class UpdateManifestContractTests
{
    [Fact]
    public void NamesAreDerivedFromStrictThreePartVersion()
    {
        var names = UpdateReleaseNames.For(new Version(0, 2, 0), "win-x64");
        Assert.Equal("PrintableBook-0.2.0-win-x64.zip", names.Archive);
        Assert.Equal("PrintableBook-0.2.0-win-x64.zip.sha256", names.Checksum);
        Assert.Equal("PrintableBook-0.2.0-win-x64.manifest.json", names.Manifest);
        Assert.Equal("PrintableBook-0.2.0-win-x64.manifest.json.sig", names.Signature);
    }

    [Theory]
    [InlineData("0.2")]
    [InlineData("0.2.0.1")]
    public void NamesRejectNonThreePartVersions(string value) => Assert.Throws<ArgumentException>(() => UpdateReleaseNames.For(Version.Parse(value), "win-x64"));

    [Theory]
    [InlineData("schema")]
    [InlineData("product")]
    [InlineData("version")]
    [InlineData("runtime")]
    [InlineData("archiveName")]
    [InlineData("checksumName")]
    [InlineData("size")]
    [InlineData("hash")]
    public void ContractRejectsInvalidManifestFields(string mutation)
    {
        var manifest = ValidManifest();
        manifest = mutation switch
        {
            "schema" => manifest with { SchemaVersion = 2 }, "product" => manifest with { Product = "Other" }, "version" => manifest with { Version = "0.2" }, "runtime" => manifest with { RuntimeIdentifier = "linux-x64" },
            "archiveName" => manifest with { Archive = manifest.Archive with { Name = "other.zip" } }, "checksumName" => manifest with { Checksum = manifest.Checksum with { Name = "other.sha256" } },
            "size" => manifest with { Archive = manifest.Archive with { SizeBytes = 0 } }, _ => manifest with { Archive = manifest.Archive with { Sha256 = new string('A', 64) } }
        };
        Assert.Throws<InvalidDataException>(() => UpdateManifestContract.Validate(manifest));
    }

    private static UpdateReleaseManifest ValidManifest() => new(1, "PrintableBook", "0.2.0", "win-x64", new UpdateReleaseAsset("PrintableBook-0.2.0-win-x64.zip", 1, new string('a', 64)), new UpdateReleaseAsset("PrintableBook-0.2.0-win-x64.zip.sha256", 1, new string('b', 64)));
}
