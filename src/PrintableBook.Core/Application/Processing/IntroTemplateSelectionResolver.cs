using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Processing;

public sealed record IntroTemplateSelectionResult(IReadOnlyList<DiscoveredIntroTemplateAsset> Assets, ProcessingFailure? Failure)
{
    public bool IsSuccess => Failure is null;
}

/// <summary>
/// Resolves the automatic Brand IntroTemplate sequence for the active processing run.
/// </summary>
public static class IntroTemplateSelectionResolver
{
    public static IntroTemplateSelectionResult Resolve(
        IReadOnlyList<DiscoveredIntroTemplateAsset>? availableAssets)
    {
        var eligible = (availableAssets ?? [])
            .Where(asset => BookSourceLayout.IsSupportedImage(asset.SourceReference))
            .OrderBy(asset => asset.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(asset => asset.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return eligible.Length == 0
            ? Failed("intro.template_empty", "The active Brand does not contain a supported IntroTemplate image.")
            : new IntroTemplateSelectionResult(eligible, null);
    }

    private static IntroTemplateSelectionResult Failed(string code, string message) =>
        new([], new ProcessingFailure(code, message));
}
