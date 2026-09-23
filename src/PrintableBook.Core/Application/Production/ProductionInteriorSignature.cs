using System.Security.Cryptography;
using System.Text;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Application.Production;

public sealed record ProductionInteriorFileFact(
    string Role,
    string Identity,
    ProductionFileSignature Signature);

public sealed record ProductionInteriorPageFact(
    string SourceKey,
    ProductionInteriorFileFact Source,
    FrameMode FrameMode);

public sealed record ProductionInteriorSignatureRecipe(
    string RenderingSignature,
    IReadOnlyList<ProductionInteriorFileFact> ProductionPrefixPages,
    IReadOnlyList<ProductionInteriorFileFact> IntroPages,
    IReadOnlyList<ProductionInteriorPageFact> InteriorPages,
    InteriorShuffleMap ShuffleMap,
    ProductionInteriorFileFact? Frame,
    bool BackgroundEnabled,
    ProductionInteriorFileFact? Background);

/// <summary>
/// Creates the canonical signature for every input that can change the published Production Interior PDF.
/// Execution-only facts such as worker concurrency and processing mode are deliberately excluded.
/// </summary>
public static class ProductionInteriorSignature
{
    public const string SchemaVersion = "production-interior-v2";

    public static bool IsCurrent(string? signature) =>
        signature?.StartsWith($"{SchemaVersion}:sha256:", StringComparison.Ordinal) == true;

    public static string Create(ProductionInteriorSignatureRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (string.IsNullOrWhiteSpace(recipe.RenderingSignature))
        {
            throw new ArgumentException("A rendering signature is required.", nameof(recipe));
        }
        if (recipe.BackgroundEnabled != (recipe.Background is not null))
        {
            throw new ArgumentException("The Background fact must match the enabled state.", nameof(recipe));
        }

        var parts = new List<string>
        {
            SchemaVersion,
            $"rendering:{recipe.RenderingSignature}",
            $"background-enabled:{recipe.BackgroundEnabled}"
        };
        AppendFiles(parts, "prefix", recipe.ProductionPrefixPages);
        AppendFiles(parts, "intro", recipe.IntroPages);
        foreach (var page in recipe.InteriorPages.OrderBy(page => page.SourceKey, StringComparer.OrdinalIgnoreCase))
        {
            parts.Add($"interior:{page.SourceKey}|{page.Source.Identity}|{Describe(page.Source.Signature)}|frame:{page.FrameMode}");
        }
        parts.Add($"frame:{(recipe.Frame is null ? "none" : $"{recipe.Frame.Identity}|{Describe(recipe.Frame.Signature)}")}");
        parts.Add($"background:{(recipe.Background is null ? "none" : $"{recipe.Background.Identity}|{Describe(recipe.Background.Signature)}")}");
        foreach (var entry in recipe.ShuffleMap.Entries.OrderBy(entry => entry.OutputIndex))
        {
            parts.Add($"shuffle:{entry.OutputIndex}|{entry.Page.Value}");
        }
        parts.Add($"shuffle-seed:{recipe.ShuffleMap.Seed?.ToString() ?? "none"}");

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', parts)))).ToLowerInvariant();
        return $"{SchemaVersion}:sha256:{digest}";
    }

    public static string CreateRenderingSignature(PrintableBookProcessingCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return CreateRenderingSignature(
            ProductionPageProcessingService.CreateSettingsSignature(command),
            command.PreparedArtworkSize,
            command.WorkingPageSize,
            command.FinalPageSize,
            command.TargetInteriorDensity,
            command.InteriorPdfPageSize);
    }

    public static string CreateRenderingSignature(GlobalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return CreateRenderingSignature(
            ProductionPageProcessingService.CreateSettingsSignature(settings),
            new ImageSize(settings.ArtworkMaximumSide, settings.ArtworkMaximumSide),
            new ImageSize(settings.WorkingPageWidth, settings.WorkingPageHeight),
            new ImageSize(settings.FinalPageWidth, settings.FinalPageHeight),
            new ImageDensity(settings.Dpi, settings.Dpi),
            settings.FinalInteriorPdfPageSize);
    }

    private static string CreateRenderingSignature(
        string pageSettingsSignature,
        ImageSize prepared,
        ImageSize working,
        ImageSize final,
        ImageDensity density,
        PhysicalPageSize pdfPageSize)
    {
        var canonical = string.Join('|',
            "production-interior-rendering-v2",
            pageSettingsSignature,
            prepared.Width,
            prepared.Height,
            working.Width,
            working.Height,
            final.Width,
            final.Height,
            density.Horizontal,
            density.Vertical,
            pdfPageSize.WidthInches,
            pdfPageSize.HeightInches,
            "frame-mode-contract-v2");
        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()}";
    }

    private static void AppendFiles(List<string> parts, string group, IReadOnlyList<ProductionInteriorFileFact> files)
    {
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            parts.Add($"{group}:{index}|{file.Role}|{file.Identity}|{Describe(file.Signature)}");
        }
    }

    private static string Describe(ProductionFileSignature signature) =>
        $"{signature.LengthBytes}|{signature.LastWriteTimeUtc.UtcTicks}";
}
