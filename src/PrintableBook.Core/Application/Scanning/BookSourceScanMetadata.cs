using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Scanning;

public enum BookSourceLayoutKind
{
    LegacyFlat,
    MainCloneNestedV1
}

public sealed record BookSourceScanMetadata(
    BookSourceLayoutKind LayoutKind,
    DirectoryReference ProcessingRoot,
    FileReference? RepresentativeImageReference = null);
