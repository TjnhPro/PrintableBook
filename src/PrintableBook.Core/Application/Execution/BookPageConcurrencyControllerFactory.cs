using System.Globalization;
using PrintableBook.Core.Configuration;

namespace PrintableBook.Core.Application.Execution;

/// <summary>
/// Resolves a page limit from an agreed configuration key supplied by the host, rather than embedding a schema in processors.
/// </summary>
public sealed class BookPageConcurrencyControllerFactory(string settingKey)
{
    private readonly string settingKey = string.IsNullOrWhiteSpace(settingKey)
        ? throw new ArgumentException("A setting key is required.", nameof(settingKey))
        : settingKey;

    public IBookPageConcurrencyController Create(EffectiveProcessingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var configuredValue = settings[settingKey];
        var configuredConcurrency = int.TryParse(configuredValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : BookPageConcurrencyController.MinimumConcurrency;

        return BookPageConcurrencyController.Create(configuredConcurrency);
    }
}
