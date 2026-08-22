using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Core.Application.Processing;

public sealed record CoverValidationRequest(FileReference Cover, ImageSize MinimumSize);

public sealed record CoverValidationResult(bool IsValid, ProcessingFailure? Failure)
{
    public static CoverValidationResult Valid() => new(true, null);

    public static CoverValidationResult Invalid(string code, string message) =>
        new(false, new ProcessingFailure(code, message));
}

/// <summary>
/// Verifies a supplied cover without changing the original asset.
/// </summary>
public interface ICoverValidator
{
    ValueTask<CoverValidationResult> ValidateAsync(
        CoverValidationRequest request,
        CancellationToken cancellationToken = default);
}
