using System.Collections.Concurrent;
using System.Security.Cryptography;
using PrintableBook.Core.Application.Production;

namespace PrintableBook.Infrastructure.Production;

public sealed class FileSystemProductionPdfNameSuggestionService : IProductionPdfNameSuggestionService
{
    internal const string CoverKeyFileName = "cover_key.txt";
    internal const string InteriorKeyFileName = "interior_key.txt";
    private const int MaximumStemLength = 251;
    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private readonly string metadataDirectory;
    private readonly Func<int, int> selectIndex;
    private readonly Lazy<IReadOnlyList<string>> coverNames;
    private readonly Lazy<IReadOnlyList<string>> interiorNames;
    private readonly ConcurrentDictionary<string, ProductionPdfNameSuggestions> suggestions =
        new(StringComparer.Ordinal);

    public FileSystemProductionPdfNameSuggestionService()
        : this(Path.Combine(AppContext.BaseDirectory, "Metadata"), RandomNumberGenerator.GetInt32)
    {
    }

    internal FileSystemProductionPdfNameSuggestionService(string metadataDirectory, Func<int, int>? selectIndex = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataDirectory);
        this.metadataDirectory = metadataDirectory;
        this.selectIndex = selectIndex ?? RandomNumberGenerator.GetInt32;
        coverNames = CreateSource(CoverKeyFileName);
        interiorNames = CreateSource(InteriorKeyFileName);
    }

    public ValueTask<ProductionPdfNameSuggestions> GetAsync(
        string bookId,
        bool regenerate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        cancellationToken.ThrowIfCancellationRequested();

        var result = regenerate
            ? suggestions.AddOrUpdate(
                bookId,
                _ => CreateSuggestions(bookId),
                (_, current) => CreateSuggestions(bookId, current))
            : suggestions.GetOrAdd(bookId, CreateSuggestions);
        return ValueTask.FromResult(result);
    }

    private ProductionPdfNameSuggestions CreateSuggestions(string bookId) =>
        CreateSuggestions(bookId, null);

    private ProductionPdfNameSuggestions CreateSuggestions(
        string bookId,
        ProductionPdfNameSuggestions? current) =>
        new(
            bookId,
            SelectName(coverNames.Value, current?.CoverFileName),
            SelectName(interiorNames.Value, current?.InteriorFileName));

    private string SelectName(IReadOnlyList<string> names, string? current)
    {
        var index = selectIndex(names.Count);
        if (index < 0 || index >= names.Count)
        {
            throw new InvalidOperationException("The PDF filename selector returned an invalid index.");
        }

        if (names.Count > 1 && string.Equals(names[index], current, StringComparison.OrdinalIgnoreCase))
        {
            index = (index + 1) % names.Count;
        }

        return names[index];
    }

    private Lazy<IReadOnlyList<string>> CreateSource(string fileName) =>
        new(() => LoadSource(fileName), LazyThreadSafetyMode.ExecutionAndPublication);

    private IReadOnlyList<string> LoadSource(string fileName)
    {
        var path = Path.Combine(metadataDirectory, fileName);
        if (!File.Exists(path))
        {
            throw new ProductionPdfNameSuggestionException(
                "pdf_name_source_missing",
                $"PDF filename source '{fileName}' is missing.");
        }

        try
        {
            var names = File.ReadLines(path)
                .Select(NormalizeName)
                .Where(name => name is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (names.Length == 0)
            {
                throw new ProductionPdfNameSuggestionException(
                    "pdf_name_source_empty",
                    $"PDF filename source '{fileName}' does not contain a valid filename.");
            }

            return names;
        }
        catch (ProductionPdfNameSuggestionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ProductionPdfNameSuggestionException(
                "pdf_name_source_unreadable",
                $"PDF filename source '{fileName}' could not be read.",
                exception);
        }
    }

    internal static string? NormalizeName(string? value)
    {
        var stem = value?.Trim();
        if (string.IsNullOrWhiteSpace(stem)) return null;
        while (stem.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            stem = stem[..^4].TrimEnd();
        }

        if (string.IsNullOrWhiteSpace(stem) || stem.Length > MaximumStemLength ||
            stem is "." or ".." || stem.EndsWith('.') || stem.EndsWith(' ') ||
            stem.Any(character => char.IsControl(character) || Path.GetInvalidFileNameChars().Contains(character)))
        {
            return null;
        }

        var deviceName = stem.Split('.', 2)[0].TrimEnd();
        return ReservedWindowsNames.Contains(deviceName) ? null : $"{stem}.pdf";
    }
}
