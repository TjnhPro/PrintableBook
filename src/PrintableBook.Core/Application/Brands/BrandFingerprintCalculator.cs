using System.Security.Cryptography;
using System.Text;
using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Brands;

public sealed class BrandFingerprintCalculator(IFileSystem fileSystem, BrandValidationTargetResolver resolver)
{
    public async ValueTask<string> CalculateAsync(DirectoryReference brandDirectory, BrandValidationDefinition definition, CancellationToken cancellationToken = default)
    {
        var manifest = new StringBuilder();
        manifest.Append("definition=").Append(definition.DefinitionChangedAtUtc.ToString("O")).Append('\n');
        foreach (var resolved in await resolver.ResolveAsync(brandDirectory, definition, cancellationToken))
        {
            manifest.Append("entry=").Append(resolved.Entry.Key).Append("|target=").Append(resolved.Entry.Target.RelativePath).Append("|rules=").Append(RulesText(resolved.Entry.Rules)).Append('\n');
            foreach (var file in resolved.Files)
            {
                var metadata = await fileSystem.GetFileMetadataAsync(file, cancellationToken);
                manifest.Append("file=").Append(BrandValidationTargetResolver.NormalizeRelativePath(brandDirectory, file));
                if (metadata is null) manifest.Append("|missing");
                else manifest.Append("|length=").Append(metadata.Value.LengthBytes).Append("|lastWriteUtcTicks=").Append(metadata.Value.LastWriteTimeUtc.UtcTicks);
                manifest.Append('\n');
            }
        }
        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest.ToString()))).ToLowerInvariant()}";
    }

    private static string RulesText(IReadOnlyList<BrandValidationRule> rules) => string.Join(',', rules.Select(rule => rule switch
    {
        BrandFileExistsRule => "exists",
        BrandImageDimensionsRule dimensions => $"dimensions:{string.Join(';', dimensions.AllowedSizes.Select(size => $"{size.Width}x{size.Height}"))}",
        _ => throw new InvalidOperationException($"Unsupported Brand validation rule '{rule.GetType().Name}'.")
    }));
}
