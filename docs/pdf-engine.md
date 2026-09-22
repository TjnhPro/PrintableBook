# PDF engine and Interior page geometry

> Xem [kiến trúc v0.1](architecture.md) và [shared Interior pipeline](interior-shared-pipeline-integration.md).

`PDFsharp` 6.2.4 writes and reopens PDF output in `PrintableBook.Infrastructure`. Magick.NET remains the raster-processing engine; it does not assemble PDFs.

## Hai raster Interior khác nhau

The tool must keep these concepts distinct:

- **Working Area** is the square processing canvas: default `2550 × 2550 px`. It is an intermediate used for centering artwork and must never determine final PDF geometry.
- **Final Interior Page** is the finished printable raster: default `2588 × 2625 px` at `300 DPI`. It is the input embedded in the Interior PDF and is the only Interior raster that determines PDF geometry.

The Interior PDF page size is derived, not configured independently:

```text
width in inches  = Final Interior Page width px / DPI
height in inches = Final Interior Page height px / DPI
width in points  = width in inches × 72
height in points = height in inches × 72
```

Therefore the default final page produces an Interior PDF MediaBox of `621.12 × 630 pt` (`8.6266667 × 8.75 in`). Rasterizing that page at 300 DPI yields `2588 × 2625 px`. The Working Area is never a PDF page. If a PDF viewer rasterizes the Interior page as `2550 × 2550 px` at 300 DPI, its MediaBox is incorrectly `8.5 × 8.5 in`; it must not be used for PDF sizing.

Cover geometry remains a separate PDF contract and is not derived from the Interior raster dimensions. Production Cover accepts exactly `5242 × 2626 px`, embeds the original raster without a pre-resize/crop, and draws it edge-to-edge on a rounded `17.47 × 8.75 inch` page (`1257.84 × 630 pt`). PNG DPI metadata does not override this page geometry.

The exporter builds Intro pages sequentially and repeated Interior units concurrently, then imports them into one final document in logical order. A Brand IntroTemplate whose raster is exactly the Final Interior Page is supplied as that finished page directly; it is not resized through the `2550 × 2550 px` Working Area and its PDF page still uses the Final Interior Page MediaBox. PDFsharp types do not appear in Core contracts; Core exposes neutral file references and physical page settings only.

Production Final Interior supplies explicit leading pages rather than relabeling them as Intro:

```text
processed Interior Cover
processed Book Owner
Intro pages
randomized Interior pages
```

The same exporter interleaves one background after every artwork when `HasBackground=true`. Both Base and Production publish atomically to `Output/<BookId> - Interior.pdf`; persisted provenance identifies which workflow last replaced that file.

References: <https://github.com/dlemstra/Magick.NET>, <https://github.com/empira/PDFsharp/blob/master/LICENSE>, and <https://github.com/empira/PDFsharp>.
