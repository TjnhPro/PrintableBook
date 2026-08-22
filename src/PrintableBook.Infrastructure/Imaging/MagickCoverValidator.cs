using ImageMagick;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Imaging;

public sealed class MagickCoverValidator : ICoverValidator
{
    public ValueTask<CoverValidationResult> ValidateAsync(
        CoverValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var image = new MagickImage(request.Cover.Value);
            if (image.Width < request.MinimumSize.Width || image.Height < request.MinimumSize.Height)
            {
                return ValueTask.FromResult(CoverValidationResult.Invalid(
                    "cover.resolution_too_small",
                    $"Cover must be at least {request.MinimumSize.Width}x{request.MinimumSize.Height} pixels."));
            }

            return ValueTask.FromResult(CoverValidationResult.Valid());
        }
        catch (MagickException)
        {
            return ValueTask.FromResult(CoverValidationResult.Invalid(
                "cover.unreadable",
                "Cover must be a readable PNG image."));
        }
        catch (IOException)
        {
            return ValueTask.FromResult(CoverValidationResult.Invalid(
                "cover.unreadable",
                "Cover must be a readable PNG image."));
        }
    }
}
