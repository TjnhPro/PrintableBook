using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Configuration;
using PrintableBook.Core.Domain.Brands;
using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Core.Application.Commands;

/// <summary>
/// Immutable input for one run after book, brand, settings, and workspace have been resolved.
/// </summary>
public sealed record ProcessingContext(
    Book Book,
    BrandProfile Brand,
    EffectiveProcessingSettings Settings,
    BookWorkspace Workspace,
    ProcessingOptions Options);
