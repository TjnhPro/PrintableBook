namespace PrintableBook.Core.Configuration;

/// <summary>
/// Supplies untyped processing settings from a configuration, environment, or runtime source.
/// Sources are applied in registration order, with later values overriding earlier values.
/// </summary>
public interface IProcessingSettingsSource
{
    ValueTask<IReadOnlyDictionary<string, string?>> LoadAsync(CancellationToken cancellationToken = default);
}
