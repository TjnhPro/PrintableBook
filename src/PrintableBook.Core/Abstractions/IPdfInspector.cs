namespace PrintableBook.Core.Abstractions;

/// <summary>
/// Reads PDF facts without leaking a third-party PDF type into Core.
/// </summary>
public interface IPdfInspector
{
    ValueTask<PdfDocumentInfo> GetInfoAsync(FileReference document, CancellationToken cancellationToken = default);
}
