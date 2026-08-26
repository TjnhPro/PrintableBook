using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Processing;

public sealed record IntroTemplateSelectionResult(IReadOnlyList<DiscoveredIntroTemplateAsset> Assets, ProcessingFailure? Failure)
{
    public bool IsSuccess => Failure is null;
}

/// <summary>
/// Resolves a persisted Book choice against the Brand that is active for this run.
/// </summary>
public static class IntroTemplateSelectionResolver
{
    public static IntroTemplateSelectionResult Resolve(
        bool hasIntro,
        IReadOnlyList<string>? selectedTemplateKeys,
        IReadOnlyList<DiscoveredIntroTemplateAsset>? availableAssets)
    {
        var eligible = (availableAssets ?? [])
            .Where(asset => BookSourceLayout.IsSupportedImage(asset.SourceReference))
            .OrderBy(asset => asset.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(asset => asset.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!hasIntro)
        {
            return eligible.Length == 0
                ? Failed("intro.template_empty", "The active Brand does not contain a supported IntroTemplate image.")
                : new IntroTemplateSelectionResult(eligible, null);
        }

        if (selectedTemplateKeys is null || selectedTemplateKeys.Count == 0)
        {
            return Failed("intro.selection_required", "Choose at least one IntroTemplate image for the custom Intro selection.");
        }

        var byKey = eligible.ToDictionary(asset => asset.Key, StringComparer.OrdinalIgnoreCase);
        var selected = new List<DiscoveredIntroTemplateAsset>(selectedTemplateKeys.Count);
        foreach (var key in selectedTemplateKeys)
        {
            string normalized;
            try
            {
                normalized = IntroTemplateSourceKey.Normalize(key);
            }
            catch (ArgumentException)
            {
                return Failed("intro.selection_missing", "A selected IntroTemplate image is no longer available from the active Brand.");
            }

            if (!byKey.TryGetValue(normalized, out var asset))
            {
                return Failed("intro.selection_missing", "A selected IntroTemplate image is no longer available from the active Brand.");
            }
            selected.Add(asset);
        }

        return new IntroTemplateSelectionResult(selected, null);
    }

    private static IntroTemplateSelectionResult Failed(string code, string message) =>
        new([], new ProcessingFailure(code, message));
}
