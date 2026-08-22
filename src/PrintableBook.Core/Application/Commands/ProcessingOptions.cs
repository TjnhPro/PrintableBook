using System.Collections.ObjectModel;

namespace PrintableBook.Core.Application.Commands;

/// <summary>
/// Optional per-run choices. Names remain opaque until a business contract exists.
/// </summary>
public sealed class ProcessingOptions
{
    public static ProcessingOptions Empty { get; } = new(new Dictionary<string, string?>());

    public ProcessingOptions(IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Values = new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>(values, StringComparer.Ordinal));
    }

    public IReadOnlyDictionary<string, string?> Values { get; }
}
