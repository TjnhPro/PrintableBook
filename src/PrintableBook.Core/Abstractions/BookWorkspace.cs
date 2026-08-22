using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Core.Abstractions;

public sealed record BookWorkspace(
    BookId BookId,
    DirectoryReference WorkingDirectory,
    DirectoryReference ProcessedDirectory,
    DirectoryReference TemporaryOutputDirectory);
