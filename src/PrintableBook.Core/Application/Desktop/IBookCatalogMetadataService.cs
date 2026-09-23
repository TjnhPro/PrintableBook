using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Desktop;

public sealed class BookCatalogMetadataException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public interface IBookCatalogMetadataService
{
    ValueTask SaveBookMetadataAsync(DiscoveredBook book, BookProductionMetadata metadata, CancellationToken cancellationToken = default);
    ValueTask AssignBrandAsync(DiscoveredBook book, DiscoveredBrand brand, CancellationToken cancellationToken = default);
    ValueTask UnassignBrandAsync(DiscoveredBook book, CancellationToken cancellationToken = default);
    ValueTask SaveBrandAuthorAsync(DiscoveredBrand brand, string? author, CancellationToken cancellationToken = default);
}

public sealed class BookCatalogMetadataService(
    IBookWorkspaceStateStore stateStore,
    IBrandMetadataStore brandMetadataStore) : IBookCatalogMetadataService
{
    public async ValueTask SaveBookMetadataAsync(
        DiscoveredBook book,
        BookProductionMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(metadata);
        var state = await LoadStateAsync(book, cancellationToken);
        await stateStore.SaveAsync(book.Workspace, state with { Metadata = metadata.Normalize() }, cancellationToken);
    }

    public async ValueTask AssignBrandAsync(
        DiscoveredBook book,
        DiscoveredBrand brand,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(brand);
        var state = await LoadStateAsync(book, cancellationToken);
        var bookAuthor = state.Metadata?.Author;
        if (string.IsNullOrWhiteSpace(bookAuthor))
        {
            throw new BookCatalogMetadataException("book_author_required", "Save a Book Author before assigning a Brand.");
        }

        var brandMetadata = await brandMetadataStore.LoadAsync(brand.Directory, cancellationToken);
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

    public ValueTask SaveBrandAuthorAsync(
        DiscoveredBrand brand,
        string? author,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(brand);
        return brandMetadataStore.SaveAsync(brand.Directory, BrandMetadata.Create(author), cancellationToken);
    }

    private async ValueTask<BookProcessingState> LoadStateAsync(DiscoveredBook book, CancellationToken cancellationToken) =>
        await stateStore.LoadAsync(book.Workspace, cancellationToken) ?? BookProcessingState.NotStarted(book.Id);
}
