using System.Collections.ObjectModel;

namespace PrintableBook.Core.Configuration;

/// <summary>
/// Immutable, resolved settings for one processing run.
/// </summary>
public sealed class EffectiveProcessingSettings
{
    private readonly IReadOnlyDictionary<string, string?> values;

    public EffectiveProcessingSettings(IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        this.values = new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>(values, StringComparer.Ordinal));
    }

    public IReadOnlyDictionary<string, string?> Values => values;

    public string? this[string key] => values.TryGetValue(key, out var value) ? value : null;
}
