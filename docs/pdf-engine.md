# PDF engine selection — Phase 2

Phase 2 uses `PDFsharp` 6.2.4 only in `PrintableBook.Infrastructure`.

PDFsharp is MIT licensed and is maintained by the PDFsharp project. It supports creating a PDF with an explicitly sized page and drawing a PNG into that physical page rectangle. This lets the application define an 8.5-inch page as 612 PDF points (8.5 × 72), while retaining the source raster rather than converting it to 612 pixels.

The exporter writes pages sequentially in the already assembled order, then reopens the completed document for inspection. PDFsharp types do not appear in Core contracts; Core exposes neutral file references and physical page settings only.

References: <https://github.com/empira/PDFsharp/blob/master/LICENSE> and <https://github.com/empira/PDFsharp>.
