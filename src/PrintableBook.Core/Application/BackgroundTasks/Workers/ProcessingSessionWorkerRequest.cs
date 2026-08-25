using PrintableBook.Core.Domain.Processing;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Application.BackgroundTasks.Workers;

public sealed record ProcessingSessionWorkerRequest(
    IReadOnlyList<string> BookIds,
    string BrandName,
    BookProcessingMode Mode,
    DateTimeOffset StartedAt);
