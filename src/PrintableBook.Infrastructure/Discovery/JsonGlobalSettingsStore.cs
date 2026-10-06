using System.Text.Json;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Infrastructure.Discovery;

public sealed class JsonGlobalSettingsStore(IApplicationRootDiscovery discovery, IFileSystem fileSystem) : IGlobalSettingsStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async ValueTask<GlobalSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        return await LoadAsync((await discovery.DiscoverAsync(cancellationToken)).Paths, cancellationToken);
    }

    public async ValueTask<GlobalSettings> LoadAsync(ApplicationPaths paths, CancellationToken cancellationToken = default)
    {
        if (!await fileSystem.FileExistsAsync(paths.SettingsFile, cancellationToken)) return NormalizeLoaded(GlobalSettings.Default);
        var settings = JsonSerializer.Deserialize<GlobalSettings>(await fileSystem.ReadTextAsync(paths.SettingsFile, cancellationToken), Options) ?? GlobalSettings.Default;
        ValidateLanguageProfiles(settings.GenericKeywordsByLanguage);
        settings = NormalizeLoaded(settings);
        Validate(settings);
        return settings;
    }

    public async ValueTask SaveAsync(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        ValidateLanguageProfiles(settings.GenericKeywordsByLanguage);
        settings = NormalizeForSave(settings);
        Validate(settings);
        var paths = (await discovery.DiscoverAsync(cancellationToken)).Paths;
        await fileSystem.WriteTextAtomicallyAsync(paths.SettingsFile, JsonSerializer.Serialize(settings, Options), cancellationToken);
    }

    private static void Validate(GlobalSettings value)
    {
        if (value.MaximumPageConcurrency is < 1 or > 12 || value.ArtworkMaximumSide <= 0 || value.WorkingPageWidth < value.ArtworkMaximumSide || value.WorkingPageHeight < value.ArtworkMaximumSide || value.FinalPageWidth < value.WorkingPageWidth || value.FinalPageHeight < value.WorkingPageHeight || value.Dpi <= 0) throw new ArgumentOutOfRangeException(nameof(value), "Global settings contain an invalid processing layout.");

        var normalization = value.EffectiveArtworkSourceNormalization;
        var borderLine = value.EffectiveBorderLineDetection;
        if (normalization.NormalizedSourceSize <= 0 ||
            borderLine.Pass1SearchDepth <= 0 ||
            borderLine.Pass2SearchDepth < borderLine.Pass1SearchDepth ||
            borderLine.Pass2SearchDepth >= normalization.NormalizedSourceSize / 2 ||
            borderLine.CornerSearchPadding < 0 ||
            borderLine.Pass2SearchDepth + borderLine.CornerSearchPadding > normalization.NormalizedSourceSize / 2 ||
            borderLine.TrackDepthTolerance < 0 || borderLine.CornerLineTolerance < 0 ||
            borderLine.MaximumDepthSpread < 0 || borderLine.SegmentCount < 1 ||
            borderLine.MinimumCompatibleCorners is < 1 or > 4 ||
            !IsRatio(borderLine.CornerExclusionRatio) || !IsRatio(borderLine.MinimumSegmentSupportRatio) ||
            !IsRatio(borderLine.MinimumSideSupportRatio) || !IsRatio(borderLine.MinimumSpanRatio) ||
            borderLine.MinimumSupportedSegments < 1 || borderLine.MinimumSupportedSegments > borderLine.SegmentCount ||
            borderLine.MaximumMissingSegmentRun < 0 || borderLine.MaximumMissingSegmentRun >= borderLine.SegmentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Global settings contain invalid artwork detection values.");
        }
    }

    private static GlobalSettings NormalizeLoaded(GlobalSettings settings)
    {
        var profiles = NormalizeProfiles(settings, preferLegacyEnglish: settings.GenericKeywords is not null);
        return Normalize(settings, profiles);
    }

    private static GlobalSettings NormalizeForSave(GlobalSettings settings)
    {
        var profiles = NormalizeProfiles(settings, preferLegacyEnglish: false);
        return Normalize(settings, profiles);
    }

    private static GlobalSettings Normalize(
        GlobalSettings settings,
        IReadOnlyDictionary<string, IReadOnlyList<string>> profiles) => settings with
    {
        ArtworkSourceNormalization = settings.EffectiveArtworkSourceNormalization,
        BorderLineDetection = settings.EffectiveBorderLineDetection,
        GenericKeywords = profiles["en"],
        GenericKeywordsByLanguage = profiles,
        S3Storage = settings.EffectiveS3Storage
    };

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> NormalizeProfiles(
        GlobalSettings settings,
        bool preferLegacyEnglish)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var language in SupportedLanguageCatalog.All)
        {
            var source = FindProfile(settings.GenericKeywordsByLanguage, language.Code);
            if (language.Code == "en" && (preferLegacyEnglish || source is null))
            {
                source = settings.GenericKeywords;
            }

            result.Add(language.Code, BookTextPolicy.NormalizePhrases(source ?? [], distinct: true));
        }

        return result;
    }

    private static IReadOnlyList<string>? FindProfile(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? profiles,
        string languageCode) =>
        profiles?.FirstOrDefault(pair => string.Equals(pair.Key, languageCode, StringComparison.OrdinalIgnoreCase)).Value;

    private static void ValidateLanguageProfiles(IReadOnlyDictionary<string, IReadOnlyList<string>>? profiles)
    {
        if (profiles is null) return;
        var canonicalCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in profiles.Keys)
        {
            if (!SupportedLanguageCatalog.TryGet(code, out var language))
            {
                throw new ArgumentException($"Unsupported Generic Keywords language code '{code}'.", nameof(profiles));
            }

            if (!canonicalCodes.Add(language.Code))
            {
                throw new ArgumentException($"Duplicate Generic Keywords language code '{language.Code}'.", nameof(profiles));
            }
        }
    }

    private static bool IsRatio(double value) => value is >= 0 and <= 1;
}
