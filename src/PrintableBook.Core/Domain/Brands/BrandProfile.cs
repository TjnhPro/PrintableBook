using System.Collections.ObjectModel;
using PrintableBook.Core.Configuration;

namespace PrintableBook.Core.Domain.Brands;

/// <summary>
/// A resolved brand profile. Resource keys remain opaque until business naming is confirmed.
/// </summary>
public sealed class BrandProfile
{
    public BrandProfile(
        BrandId id,
        EffectiveProcessingSettings? settings = null,
        IReadOnlyDictionary<string, string?>? resources = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Settings = settings;
        Resources = new ReadOnlyDictionary<string, string?>(
            new Dictionary<string, string?>(resources ?? new Dictionary<string, string?>(), StringComparer.Ordinal));
    }

    public BrandId Id { get; }

    public EffectiveProcessingSettings? Settings { get; }

    public IReadOnlyDictionary<string, string?> Resources { get; }
}
