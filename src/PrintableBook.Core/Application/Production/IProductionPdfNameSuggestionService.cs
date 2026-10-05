namespace PrintableBook.Core.Application.Production;

public sealed record ProductionPdfNameSuggestions(
    string BookId,
    string CoverFileName,
    string InteriorFileName);

public sealed class ProductionPdfNameSuggestionException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

public interface IProductionPdfNameSuggestionService
{
    ValueTask<ProductionPdfNameSuggestions> GetAsync(
        string bookId,
        bool regenerate,
        CancellationToken cancellationToken = default);
}
