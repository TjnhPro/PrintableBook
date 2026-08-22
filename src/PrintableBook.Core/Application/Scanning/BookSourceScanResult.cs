using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Scanning;

public sealed record BookSourceScanResult(BookSource? Source, ProcessingFailure? Failure)
{
    public bool IsSuccess => Source is not null && Failure is null;

    public static BookSourceScanResult Succeeded(BookSource source) => new(source, null);

    public static BookSourceScanResult Failed(ProcessingFailure failure, BookSource? source = null) => new(source, failure);
}
