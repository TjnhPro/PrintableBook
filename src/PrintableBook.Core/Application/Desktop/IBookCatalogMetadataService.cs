using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Desktop;

public sealed class BookCatalogMetadataException(
    string code,
    string message,
    IReadOnlyList<BookMetadataValidationError>? validationErrors = null) : InvalidOperationException(message)
{
    public string Code { get; } = code;
    public IReadOnlyList<BookMetadataValidationError>? ValidationErrors { get; } = validationErrors;
}

public interface IBookCatalogMetadataService
{
    ValueTask SaveBookMetadataAsync(DiscoveredBook book, BookProductionMetadata metadata, CancellationToken cancellationToken = default);
    ValueTask<BookKeywordBuilderState> SaveKeywordBuilderAsync(DiscoveredBook book, IReadOnlyList<string> keywords, string? adsAsin, CancellationToken cancellationToken = default);
    ValueTask AssignBrandAsync(DiscoveredBook book, DiscoveredBrand brand, CancellationToken cancellationToken = default);
    ValueTask UnassignBrandAsync(DiscoveredBook book, CancellationToken cancellationToken = default);
    ValueTask SaveBrandAuthorAsync(DiscoveredBrand brand, string? author, CancellationToken cancellationToken = default);
}

public sealed class BookCatalogMetadataService(
    IBookWorkspaceStateStore stateStore,
    IBrandMetadataStore brandMetadataStore,
    BookKeywordBuilder? keywordBuilder = null,
    IGlobalSettingsStore? settingsStore = null) : IBookCatalogMetadataService
{
    private readonly BookKeywordBuilder keywordBuilder = keywordBuilder ?? new BookKeywordBuilder();

    public async ValueTask SaveBookMetadataAsync(
        DiscoveredBook book,
        BookProductionMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(metadata);
        var validationErrors = metadata.ValidateForSave();
        if (validationErrors.Count > 0)
        {
            throw new BookCatalogMetadataException(
                "invalid_book_metadata",
                "Book Information contains invalid values.",
                validationErrors);
        }

        var state = await LoadStateAsync(book, cancellationToken);
        await stateStore.SaveAsync(book.Workspace, state with { Metadata = metadata.Normalize() }, cancellationToken);
    }

    public async ValueTask<BookKeywordBuilderState> SaveKeywordBuilderAsync(
        DiscoveredBook book,
        IReadOnlyList<string> keywords,
        string? adsAsin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(keywords);
        var state = await LoadStateAsync(book, cancellationToken);
        var genericKeywords = settingsStore is null
            ? []
            : (await settingsStore.LoadAsync(cancellationToken)).GetEffectiveGenericKeywords(state.LanguageCode);
        var result = keywordBuilder.Build(genericKeywords, keywords, adsAsin, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
        await stateStore.SaveAsync(book.Workspace, state with { KeywordBuilder = result }, cancellationToken);
        return result;
    }

    public async ValueTask AssignBrandAsync(
        DiscoveredBook book,
        DiscoveredBrand brand,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(brand);
        var state = await LoadStateAsync(book, cancellationToken);
        var bookLanguage = SupportedLanguageCatalog.GetEffective(state.LanguageCode);
        var brandMetadata = await brandMetadataStore.LoadAsync(brand.Directory, cancellationToken);
        var brandLanguage = SupportedLanguageCatalog.GetEffective(brandMetadata?.LanguageCode);
        if (!string.Equals(bookLanguage.Code, brandLanguage.Code, StringComparison.Ordinal))
        {
            throw new BookCatalogMetadataException(
                "book_brand_language_mismatch",
                $"Book Language '{bookLanguage.Name}' must match Brand Language '{brandLanguage.Name}' before assignment.");
        }

        var bookAuthor = state.Metadata?.Author;
        if (string.IsNullOrWhiteSpace(bookAuthor))
        {
            throw new BookCatalogMetadataException("book_author_required", "Save a Book Author before assigning a Brand.");
        }

        if (string.IsNullOrWhiteSpace(brandMetadata?.Author))
        {
            throw new BookCatalogMetadataException("brand_author_required", $"Brand '{brand.Name}' does not have an Author.");
        }

        if (!AuthorMatchPolicy.IsMatch(bookAuthor, brandMetadata.Author))
        {
            throw new BookCatalogMetadataException("book_brand_author_mismatch", "Book Author must match Brand Author before assignment.");
        }

        await stateStore.SaveAsync(book.Workspace, state with { AssignedBrand = brand.Name }, cancellationToken);
    }

    public async ValueTask UnassignBrandAsync(DiscoveredBook book, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        var state = await LoadStateAsync(book, cancellationToken);
        await stateStore.SaveAsync(book.Workspace, state with { AssignedBrand = null }, cancellationToken);
    }

    public async ValueTask SaveBrandAuthorAsync(
        DiscoveredBrand brand,
        string? author,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(brand);
        var existing = await brandMetadataStore.LoadAsync(brand.Directory, cancellationToken);
        await brandMetadataStore.SaveAsync(
            brand.Directory,
            BrandMetadata.Create(author, existing?.LanguageCode),
            cancellationToken);
    }

    private async ValueTask<BookProcessingState> LoadStateAsync(DiscoveredBook book, CancellationToken cancellationToken) =>
        await stateStore.LoadAsync(book.Workspace, cancellationToken) ?? BookProcessingState.NotStarted(book.Id);
}
