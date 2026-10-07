using PrintableBook.Core.Application.Discovery;

namespace PrintableBook.Core.Application.Brands;

public interface IBrandCloneService
{
    ValueTask<BrandCloneResult> CloneAsync(
        ApplicationPaths paths,
        DiscoveredBrand sourceBrand,
        SupportedLanguageOption language,
        CancellationToken cancellationToken = default);
}

public sealed record BrandCloneResult(
    DiscoveredBrand SourceBrand,
    SupportedLanguageOption Language,
    DiscoveredBrand DestinationBrand);

public sealed class BrandCloneException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
