using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;

namespace PrintableBook.Core.Application.Brands;

public abstract record BrandValidationTarget(string RelativePath);

public sealed record BrandValidationFileTarget(string RelativePath)
    : BrandValidationTarget(RelativePath);

public sealed record BrandValidationDirectoryFilesTarget(
    string RelativePath,
    bool Recursive,
    IReadOnlySet<string> Extensions,
    int MinimumFileCount)
    : BrandValidationTarget(RelativePath);

public abstract record BrandValidationRule;

public sealed record BrandFileExistsRule : BrandValidationRule;

public sealed record BrandImageDimensionsRule(IReadOnlyList<ImageSize> AllowedSizes)
    : BrandValidationRule;

public sealed record BrandValidationEntry(
    string Key,
    BrandValidationTarget Target,
    IReadOnlyList<BrandValidationRule> Rules);

public sealed record BrandValidationDefinition(
    DateTimeOffset DefinitionChangedAtUtc,
    IReadOnlyList<BrandValidationEntry> Entries)
{
    private static readonly IReadOnlySet<string> SupportedIntroExtensions =
        new HashSet<string>([".png", ".jpg", ".jpeg"], StringComparer.OrdinalIgnoreCase);

    // Any future tracked-scope or rule semantic change must update this to its real UTC change time.
    private static readonly DateTimeOffset CurrentDefinitionChangedAtUtc =
        new(2026, 8, 31, 4, 32, 0, TimeSpan.Zero);

    public static BrandValidationDefinition CreateCurrent(GlobalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new BrandValidationDefinition(
            CurrentDefinitionChangedAtUtc,
            [
                new BrandValidationEntry(
                    "intro",
                    new BrandValidationDirectoryFilesTarget("IntroTemplate", true, SupportedIntroExtensions, 1),
                    [
                        new BrandFileExistsRule(),
                        new BrandImageDimensionsRule([new ImageSize(1024, 1024), new ImageSize(2048, 2048)])
                    ]),
                new BrandValidationEntry(
                    "frame",
                    new BrandValidationFileTarget("frame.png"),
                    [
                        new BrandFileExistsRule(),
                        new BrandImageDimensionsRule([new ImageSize(settings.ArtworkMaximumSide, settings.ArtworkMaximumSide)])
                    ]),
                new BrandValidationEntry(
                    "background",
                    new BrandValidationFileTarget("background.png"),
                    [
                        new BrandFileExistsRule(),
                        new BrandImageDimensionsRule([new ImageSize(settings.FinalPageWidth, settings.FinalPageHeight)])
                    ])
            ]);
    }
}
