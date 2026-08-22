using System.Text.Json;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Abstractions;

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
        if (!await fileSystem.FileExistsAsync(paths.SettingsFile, cancellationToken)) return GlobalSettings.Default;
        return JsonSerializer.Deserialize<GlobalSettings>(await fileSystem.ReadTextAsync(paths.SettingsFile, cancellationToken), Options) ?? GlobalSettings.Default;
    }

    public async ValueTask SaveAsync(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        Validate(settings);
        var paths = (await discovery.DiscoverAsync(cancellationToken)).Paths;
        await fileSystem.WriteTextAtomicallyAsync(paths.SettingsFile, JsonSerializer.Serialize(settings, Options), cancellationToken);
    }

    private static void Validate(GlobalSettings value)
    {
        if (value.MaximumPageConcurrency is < 1 or > 12 || value.ArtworkMaximumSide <= 0 || value.WorkingPageWidth < value.ArtworkMaximumSide || value.WorkingPageHeight < value.ArtworkMaximumSide || value.FinalPageWidth < value.WorkingPageWidth || value.FinalPageHeight < value.WorkingPageHeight || value.Dpi <= 0 || value.InteriorPdfWidthInches <= 0 || value.InteriorPdfHeightInches <= 0) throw new ArgumentOutOfRangeException(nameof(value), "Global settings contain an invalid processing layout.");
    }
}
