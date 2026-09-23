using PrintableBook.Core.Domain.Processing;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Application.BackgroundTasks.Workers;

public sealed record ProcessingSessionWorkerRequest(
    IReadOnlyList<string> BookIds,
    BookProcessingMode Mode,
    DateTimeOffset StartedAt);
