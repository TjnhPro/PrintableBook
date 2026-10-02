using PrintableBook.Core.Domain.Books;

namespace PrintableBook.Core.Application.Desktop;

public sealed record KeywordPreviewResult(
    BookKeywordBuilderState Preview,
    string Receipt,
    string ReceiptDigest);

public sealed record KeywordPreviewSaveResult(
    BookKeywordBuilderState KeywordBuilder,
    string Disposition);

public sealed record KeywordCrawlSource(
    IReadOnlyList<string> Keywords,
    string SourceFingerprint,
    string? ReceiptDigest);

public sealed record KeywordPreviewError(
    string Code,
    string Message,
    string? Field = null,
    string RetryAction = "none");

public sealed class KeywordPreviewException(KeywordPreviewError error) : InvalidOperationException(error.Message)
{
    public KeywordPreviewError Error { get; } = error;
}

public sealed record KeywordPreviewReceiptPayload(
    int ReceiptVersion,
    string BookId,
    string BuildId,
    long BuiltAtUnixMilliseconds,
    int AlgorithmVersion,
    string ShuffleSeed,
    IReadOnlyList<string> BookKeywords,
    string? AdsAsinSource,
    string GenericFingerprint,
    string InputFingerprint,
    string OutputDigest);
