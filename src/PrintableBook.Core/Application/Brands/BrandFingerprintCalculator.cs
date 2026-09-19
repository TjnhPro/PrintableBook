using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Brands;

public sealed record BrandAssetFingerprint(
    string Value,
    IReadOnlyList<string> ExistingRelativePaths);

public sealed class BrandFingerprintCalculator(IFileSystem fileSystem, BrandValidationTargetResolver resolver)
{
    public const int DefinitionSignatureFormatVersion = 2;
    public const int AssetFingerprintFormatVersion = 2;

    public string CalculateDefinitionSignature(BrandValidationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var manifest = new StringBuilder();
        manifest.Append("definitionFormatVersion=").Append(DefinitionSignatureFormatVersion).Append('\n');
        manifest.Append("definitionChangedAtUtc=").Append(definition.DefinitionChangedAtUtc.ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        foreach (var entry in definition.Entries.OrderBy(CanonicalEntryKey, StringComparer.Ordinal))
        {
            manifest.Append("entry=").Append(Encode(entry.Key)).Append('\n');
            switch (entry.Target)
            {
                case BrandValidationFileTarget file:
                    manifest.Append("targetKind=file|path=").Append(Encode(NormalizeDefinitionPath(file.RelativePath))).Append('\n');
                    break;
                case BrandValidationDirectoryFilesTarget directory:
                    manifest.Append("targetKind=directory|path=").Append(Encode(NormalizeDefinitionPath(directory.RelativePath)))
                        .Append("|recursive=").Append(directory.Recursive ? "true" : "false")
                        .Append("|minimumFileCount=").Append(directory.MinimumFileCount.ToString(CultureInfo.InvariantCulture))
                        .Append("|extensions=").Append(string.Join(',', directory.Extensions.Select(NormalizeExtension).Order(StringComparer.Ordinal).Select(Encode)))
                        .Append('\n');
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported Brand validation target '{entry.Target.GetType().Name}'.");
            }

            foreach (var rule in entry.Rules.Select(RuleText).Order(StringComparer.Ordinal))
            {
                manifest.Append("rule=").Append(rule).Append('\n');
            }
        }

        return Hash(manifest);
    }

    public async ValueTask<BrandAssetFingerprint> CaptureAssetFingerprintAsync(
        DirectoryReference brandDirectory,
        BrandValidationDefinition definition,
        CancellationToken cancellationToken = default)
    {
        var resolved = await resolver.ResolveAsync(brandDirectory, definition, cancellationToken);
        return await CaptureAssetFingerprintAsync(brandDirectory, resolved, cancellationToken);
    }

    public async ValueTask<BrandAssetFingerprint> CaptureAssetFingerprintAsync(
        DirectoryReference brandDirectory,
        IReadOnlyList<ResolvedBrandValidationEntry> resolvedEntries,
        CancellationToken cancellationToken = default)
    {
        var manifest = new StringBuilder();
        var existingPaths = new List<string>();
        manifest.Append("assetFingerprintFormatVersion=").Append(AssetFingerprintFormatVersion).Append('\n');
        foreach (var resolved in resolvedEntries.OrderBy(item => CanonicalEntryKey(item.Entry), StringComparer.Ordinal))
        {
            manifest.Append("entry=").Append(Encode(resolved.Entry.Key)).Append('\n');
            foreach (var file in resolved.Files.OrderBy(file => BrandValidationTargetResolver.NormalizeRelativePath(brandDirectory, file), StringComparer.Ordinal))
            {
                var relativePath = BrandValidationTargetResolver.NormalizeRelativePath(brandDirectory, file);
                var metadata = await fileSystem.GetFileMetadataAsync(file, cancellationToken);
                manifest.Append("file=").Append(Encode(relativePath));
                if (metadata is null)
                {
                    manifest.Append("|missing");
                }
                else
                {
                    existingPaths.Add(relativePath);
                    manifest.Append("|length=").Append(metadata.Value.LengthBytes.ToString(CultureInfo.InvariantCulture))
                        .Append("|lastWriteUtcTicks=").Append(metadata.Value.LastWriteTimeUtc.UtcTicks.ToString(CultureInfo.InvariantCulture));
                }
                manifest.Append('\n');
            }
        }

        return new BrandAssetFingerprint(Hash(manifest), existingPaths.Order(StringComparer.Ordinal).ToArray());
    }

    public async ValueTask<string> CalculateAsync(
        DirectoryReference brandDirectory,
        BrandValidationDefinition definition,
        CancellationToken cancellationToken = default) =>
        (await CaptureAssetFingerprintAsync(brandDirectory, definition, cancellationToken)).Value;

    private static string CanonicalEntryKey(BrandValidationEntry entry) =>
        $"{Encode(entry.Key)}|{Encode(NormalizeDefinitionPath(entry.Target.RelativePath))}";

    private static string RuleText(BrandValidationRule rule) => rule switch
    {
        BrandFileExistsRule => "exists",
        BrandImageDimensionsRule dimensions => $"dimensions:{string.Join(';', dimensions.AllowedSizes.OrderBy(size => size.Width).ThenBy(size => size.Height).Select(size => $"{size.Width.ToString(CultureInfo.InvariantCulture)}x{size.Height.ToString(CultureInfo.InvariantCulture)}"))}",
        _ => throw new InvalidOperationException($"Unsupported Brand validation rule '{rule.GetType().Name}'.")
    };

    private static string NormalizeDefinitionPath(string value) =>
        BrandValidationTargetResolver.NormalizeRelativePath(value);

    private static string NormalizeExtension(string value) => value.Trim().ToLowerInvariant();

    private static string Encode(string value) =>
        Convert.ToHexString(Encoding.UTF8.GetBytes(value)).ToLowerInvariant();

    private static string Hash(StringBuilder manifest) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest.ToString()))).ToLowerInvariant()}";
}
