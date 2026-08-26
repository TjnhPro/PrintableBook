using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Scanning;

public sealed record BookSourceValidationResult(BookSource Source, ProcessingFailure? Failure)
{
    public bool IsSuccess => Failure is null;

    public static BookSourceValidationResult Succeeded(BookSource source) => new(source, null);

    public static BookSourceValidationResult Failed(BookSource source, ProcessingFailure failure) => new(source, failure);
}
