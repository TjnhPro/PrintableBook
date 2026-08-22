# PDF engine selection — Phase 2

Phase 2 uses Magick.NET's PDF encoder for writing and `PDFsharp` 6.2.4 only in `PrintableBook.Infrastructure` for reopening and inspecting output.

Magick.NET already provides the required real PNG/PDF encoding capability in the image engine selected for this phase. The PDF output page size is set through the image density while retaining the original raster dimensions. For example, a 2550-pixel square at 300 DPI produces an 8.5-inch (612-point) PDF page, not a 612-pixel raster. PDFsharp is MIT licensed and supplies independent PDF reopening/inspection in tests.

The exporter writes pages sequentially in the already assembled order, then reopens the completed document for inspection. PDFsharp types do not appear in Core contracts; Core exposes neutral file references and physical page settings only.

References: <https://github.com/dlemstra/Magick.NET>, <https://github.com/empira/PDFsharp/blob/master/LICENSE>, and <https://github.com/empira/PDFsharp>.
