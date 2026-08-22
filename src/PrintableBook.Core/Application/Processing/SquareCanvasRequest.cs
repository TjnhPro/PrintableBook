using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

public sealed record SquareCanvasRequest(FileReference Source, FileReference Target);
