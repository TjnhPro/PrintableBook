namespace PrintableBook.Core.Configuration;

/// <summary>
/// Resolves an immutable settings snapshot before a processing run begins.
/// </summary>
public sealed class ProcessingSettingsResolver(IEnumerable<IProcessingSettingsSource> sources)
{
    private readonly IReadOnlyList<IProcessingSettingsSource> sources = sources?.ToArray()
        ?? throw new ArgumentNullException(nameof(sources));

    public async ValueTask<EffectiveProcessingSettings> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var effectiveValues = new Dictionary<string, string?>(StringComparer.Ordinal);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = await source.LoadAsync(cancellationToken);

            foreach (var (key, value) in values)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new InvalidOperationException("Processing setting keys cannot be blank.");
                }

                effectiveValues[key] = value;
            }
        }

        return new EffectiveProcessingSettings(effectiveValues);
    }
}
