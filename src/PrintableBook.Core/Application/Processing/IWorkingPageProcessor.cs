using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

/// <summary>Centers unscaled artwork on a white working page using floor offsets for odd margins.</summary>
public interface IWorkingPageProcessor
{
    ValueTask CenterAsync(WorkingPageRequest request, CancellationToken cancellationToken = default);
}

public sealed record WorkingPageRequest(FileReference Source, FileReference Target, ImageSize PageSize);
