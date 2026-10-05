using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Infrastructure.Pdf;
using PrintableBook.Infrastructure.Workspaces;

namespace PrintableBook.Desktop.Tests;

public sealed class BookWorkspaceLayoutContractTests
{
    [Fact]
    public void WebViewShellFillsTheAvailableViewportWithoutAForcedMinimumPageWidth()
    {
        var frontend = Path.Combine(AppContext.BaseDirectory, "Frontend");
        var markup = File.ReadAllText(Path.Combine(frontend, "index.html"));
        var layout = File.ReadAllText(Path.Combine(frontend, "css", "book-workspace.css"));

        Assert.DoesNotContain("min-w-[1600px]", markup, StringComparison.Ordinal);
        Assert.Contains("pb-app-canvas", markup, StringComparison.Ordinal);
        Assert.Contains("--pb-design-width: 1600px", layout, StringComparison.Ordinal);
        Assert.Contains("--pb-design-height: 900px", layout, StringComparison.Ordinal);
        Assert.Contains("width: 100%", layout, StringComparison.Ordinal);
        Assert.Contains("height: 100dvh", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("width: min(100vw, var(--pb-webview-width))", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("height: min(100dvh, var(--pb-webview-height))", layout, StringComparison.Ordinal);
        Assert.Contains("overflow-x: hidden", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void BookLibraryContractUsesPaginatedMasterDetailAndCompactStatusSelect()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "js", "app.js"));

        Assert.Contains("const pageSize = 12", script, StringComparison.Ordinal);
        Assert.Contains("Needs review", script, StringComparison.Ordinal);
        Assert.Contains("PDF ready", script, StringComparison.Ordinal);
        Assert.Contains("book-master-detail", script, StringComparison.Ordinal);
        Assert.Contains("book-list-row", script, StringComparison.Ordinal);
        Assert.Contains("const bookThumbnailMarkup", script, StringComparison.Ordinal);
        Assert.Contains("bookThumbnailMarkup(item, itemSummary)", script, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("book-frame-filter", script, StringComparison.Ordinal);
        Assert.DoesNotContain("bookFrameFilter", script, StringComparison.Ordinal);
        Assert.DoesNotContain("clear-book-filters", script, StringComparison.Ordinal);
        Assert.Contains("data-action=\"clear-artwork-filters\"", script, StringComparison.Ordinal);
        Assert.Contains("book-status-filter", script, StringComparison.Ordinal);
        Assert.Contains("data-action=\"book-status\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("book-status-filters", script, StringComparison.Ordinal);
        Assert.DoesNotContain("toggle-book-selection", script, StringComparison.Ordinal);
        Assert.DoesNotContain("toggle-book-page-selection", script, StringComparison.Ordinal);

        var layout = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "css", "book-workspace.css"));
        Assert.Contains(".book-library-page { display:grid", layout, StringComparison.Ordinal);
        Assert.Contains(".book-master-detail { display:grid; grid-template-columns:minmax(250px,3fr) minmax(0,9fr)", layout, StringComparison.Ordinal);
        Assert.Contains(".book-list-scroll { display:grid; align-content:start; min-height:0", layout, StringComparison.Ordinal);
        Assert.Contains(".book-status-filter { min-width:0; }", layout, StringComparison.Ordinal);
        Assert.DoesNotContain(".book-status-filters", layout, StringComparison.Ordinal);
        Assert.Contains(".book-pagination { display:flex", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void BookDetailUsesAnAccessibleInlinePanel()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "js", "app.js"));
        var layout = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "css", "book-workspace.css"));

        Assert.Contains("book-detail-panel", script, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"book-detail-title\"", script, StringComparison.Ordinal);
        Assert.Contains("book-detail-empty", script, StringComparison.Ordinal);
        Assert.DoesNotContain("book-drawer-layer", script, StringComparison.Ordinal);
        Assert.DoesNotContain("close-book-drawer", script, StringComparison.Ordinal);
        Assert.Contains(".book-detail-panel { display:grid", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("@keyframes pb-bottom-sheet-in", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void BookRepresentativePreviewsAreSquareAndCenterCroppedWithoutChangingPdfGeometry()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "js", "app.js"));
        var layout = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "css", "book-workspace.css"));

        Assert.Contains("--pb-representative-preview: 1 / 1", layout, StringComparison.Ordinal);
        Assert.Contains(".book-list-thumbnail { display:grid; width:68px; aspect-ratio:1", layout, StringComparison.Ordinal);
        Assert.Contains(".book-list-thumbnail img { width:100%; height:100%; object-fit:cover; object-position:center center; }", layout, StringComparison.Ordinal);
        Assert.Contains(".book-detail-preview img { width:100%; height:100%; object-fit:cover; object-position:center center; }", layout, StringComparison.Ordinal);
        Assert.Contains(".book-detail-preview { display:grid; width:52px; height:52px", layout, StringComparison.Ordinal);
        Assert.Contains(".book-detail-preview { width:48px; height:48px; }", layout, StringComparison.Ordinal);
        Assert.Contains("book-detail-title-line", script, StringComparison.Ordinal);
        Assert.DoesNotContain("<p class=\"eyebrow\">Book detail</p>", script, StringComparison.Ordinal);
        Assert.Contains("--pb-book-card-preview: 4 / 3", layout, StringComparison.Ordinal);
        Assert.Contains(".pdf-library-book-grid .pdf-library-book-preview { aspect-ratio:2 / 1; }", layout, StringComparison.Ordinal);
        Assert.Contains(".pdf-library-book-preview img { width:100%; height:100%; object-fit:contain;", layout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PdfLibraryCoverThumbnailIsPublishedFromTheFinalCoverRasterAndSelectedByTheCard()
    {
        var root = Path.Combine(Path.GetTempPath(), $"PrintableBook.PdfLibraryCover.{Guid.NewGuid():N}");
        var temporaryOutput = new DirectoryReference(Path.Combine(root, ".workspace", "cover-output"));
        var finalOutput = new DirectoryReference(Path.Combine(root, "Output"));
        Directory.CreateDirectory(root);
        try
        {
            var sourcePath = Path.Combine(root, "final-cover.png");
            using (var image = new MagickImage(MagickColors.CornflowerBlue, 5242, 2626))
            {
                image.Write(sourcePath);
            }

            var exported = await new PdfSharpPrintableBookPdfExporter().ExportCoverAsync(
                new CoverPdfExportRequest(
                    new FileReference(sourcePath),
                    temporaryOutput,
                    new PhysicalPageSize(17.47, 8.75)));
            await new ValidatedBookOutputPublisher(new PdfSharpDocumentInspector()).PublishCoverAsync(
                new CoverOutputPublicationRequest(
                    new BookId("Book One"),
                    exported,
                    finalOutput,
                    1,
                    new PhysicalPageSize(17.47, 8.75)));

            var thumbnailPath = Path.Combine(finalOutput.Value, "Book One - Cover_thumbnail.png");
            Assert.True(File.Exists(thumbnailPath));
            using var thumbnail = new MagickImage(thumbnailPath);
            Assert.Equal(2726u, thumbnail.Width);
            Assert.Equal(1313u, thumbnail.Height);

            var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "js", "app.js"));
            Assert.Contains("thumbnailImageUrl", script, StringComparison.Ordinal);
            Assert.Contains("pdfLibraryCoverThumbnailMarkup", script, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FullBookBuildAlsoPublishesThePdfLibraryCoverThumbnail()
    {
        var root = Path.Combine(Path.GetTempPath(), $"PrintableBook.FullBuildCover.{Guid.NewGuid():N}");
        var temporaryOutput = new DirectoryReference(Path.Combine(root, ".workspace", "full-output"));
        var finalOutput = new DirectoryReference(Path.Combine(root, "Output"));
        Directory.CreateDirectory(root);
        try
        {
            var coverPath = Path.Combine(root, "final-cover.png");
            var interiorPath = Path.Combine(root, "interior.png");
            using (var cover = new MagickImage(MagickColors.CornflowerBlue, 5242, 2626)) cover.Write(coverPath);
            using (var interior = new MagickImage(MagickColors.White, 600, 609)) interior.Write(interiorPath);

            var exported = await new PdfSharpPrintableBookPdfExporter().ExportAsync(
                new PrintableBookPdfExportRequest(
                    new FileReference(coverPath),
                    [],
                    [new FileReference(interiorPath)],
                    null,
                    temporaryOutput,
                    new PhysicalPageSize(17.47, 8.75),
                    new PhysicalPageSize(8.5, 8.5),
                    1));
            await new ValidatedBookOutputPublisher(new PdfSharpDocumentInspector()).PublishAsync(
                new BookOutputPublicationRequest(
                    new BookId("Book One"),
                    exported,
                    finalOutput,
                    new PrintableBookPdfValidation(
                        1,
                        1,
                        new PhysicalPageSize(17.47, 8.75),
                        new PhysicalPageSize(8.5, 8.5))));

            var thumbnailPath = Path.Combine(finalOutput.Value, "Book One - Cover_thumbnail.png");
            Assert.True(File.Exists(thumbnailPath));
            using var thumbnail = new MagickImage(thumbnailPath);
            Assert.Equal(2726u, thumbnail.Width);
            Assert.Equal(1313u, thumbnail.Height);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InteriorArtworkWorkspaceUsesAnIndependentlyScrollableStatusGrid()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "js", "app.js"));
        var layout = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "css", "book-workspace.css"));

        Assert.Contains("const localImageMarkup", script, StringComparison.Ordinal);
        Assert.Contains("width=\"256\" height=\"256\" loading=\"lazy\" decoding=\"async\" data-local-image", script, StringComparison.Ordinal);
        Assert.Contains("content.addEventListener(\"error", script, StringComparison.Ordinal);
        Assert.DoesNotContain("queueVisible" + "AssetPreviews", script, StringComparison.Ordinal);
        Assert.DoesNotContain("book.asset" + ".preview", script, StringComparison.Ordinal);
        Assert.Contains("interior-artwork-grid-scroll", script, StringComparison.Ordinal);
        Assert.Contains("Review every available Book interior page", script, StringComparison.Ordinal);
        Assert.Contains("asset-status", script, StringComparison.Ordinal);
        Assert.Contains("asset-frame-mode", script, StringComparison.Ordinal);
        Assert.Contains("artworkGridScrollTop", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Search artwork", script, StringComparison.Ordinal);
        Assert.DoesNotContain("filter-assets", script, StringComparison.Ordinal);
        Assert.DoesNotContain("assetSearchFocused", script, StringComparison.Ordinal);
        Assert.Contains("selectedArtworkReferences", script, StringComparison.Ordinal);
        Assert.Contains("toggle-all-artwork", script, StringComparison.Ordinal);
        Assert.Contains("apply-artwork-bulk", script, StringComparison.Ordinal);
        Assert.Contains("refreshInteriorArtworkWorkspace", script, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=\"${selected}\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("data-action=\"set-interior-active\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("data-action=\"set-interior-frame-mode\"", script, StringComparison.Ordinal);
        Assert.Contains("--pb-asset-preview: 1 / 1", layout, StringComparison.Ordinal);
        Assert.Contains(".book-detail-body:has(.tab-body-artwork)", layout, StringComparison.Ordinal);
        Assert.Contains("overflow-y:auto", layout, StringComparison.Ordinal);
        Assert.Contains("<legend>Artwork controls</legend>", script, StringComparison.Ordinal);
        Assert.Contains("data-action=\"random-interior\"", script, StringComparison.Ordinal);
        Assert.Contains("send(\"book.interior.shuffle\"", script, StringComparison.Ordinal);
        Assert.Contains("Save Interior changes first", script, StringComparison.Ordinal);
        Assert.Contains("Random Interior using the current active artwork before processing.", script, StringComparison.Ordinal);
        Assert.Contains("interior-shuffle-status", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Random order ready", script, StringComparison.Ordinal);
        Assert.Contains(".interior-shuffle-button", layout, StringComparison.Ordinal);
        Assert.Contains("class=\"interior-artwork-actions\"", script, StringComparison.Ordinal);
        Assert.Contains(".interior-artwork-actions { display:flex", layout, StringComparison.Ordinal);
        Assert.Contains(".interior-artwork-grid { display:grid; grid-template-columns:repeat(4,minmax(0,1fr))", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("@media (min-width:1450px) { .interior-artwork-grid", layout, StringComparison.Ordinal);
        Assert.Contains(".interior-artwork-card.is-selected", layout, StringComparison.Ordinal);
        Assert.Contains("button.interior-artwork-card:focus-visible", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void BookSettingsExposesGroupedCardsAndHidesLegacyInteriorTabs()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "js", "app.js"));
        var layout = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "css", "book-workspace.css"));

        Assert.Contains("data-action=\"set-book-background\"", script, StringComparison.Ordinal);
        Assert.Contains("tabButton(\"settings\", \"Settings\")", script, StringComparison.Ordinal);
        Assert.DoesNotContain("tabButton(\"overview\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("tabButton(\"pages\", \"Interior pages\")", script, StringComparison.Ordinal);
        Assert.Contains("tabButton(\"artwork\", \"Interior artwork\")", script, StringComparison.Ordinal);
        Assert.Contains("class=\"book-settings-workspace\"", script, StringComparison.Ordinal);
        Assert.Contains("book-settings-information", script, StringComparison.Ordinal);
        Assert.Contains("book-settings-assignment", script, StringComparison.Ordinal);
        Assert.Contains("book-settings-background", script, StringComparison.Ordinal);
        Assert.Contains("book-settings-templates", script, StringComparison.Ordinal);
        Assert.Contains("<legend>Book Information</legend>", script, StringComparison.Ordinal);
        Assert.Contains("<legend>Brand Assignment</legend>", script, StringComparison.Ordinal);
        Assert.Contains("<legend>Brand background</legend>", script, StringComparison.Ordinal);
        Assert.Contains("<legend>Brand PSD templates</legend>", script, StringComparison.Ordinal);
        Assert.Contains(".book-settings-card>legend", layout, StringComparison.Ordinal);
        Assert.Contains(".book-settings-information { grid-column:span 8; }", layout, StringComparison.Ordinal);
        Assert.Contains(".book-settings-assignment { grid-column:span 4; }", layout, StringComparison.Ordinal);
        Assert.Contains(".book-settings-background,.book-settings-templates { grid-column:span 6; }", layout, StringComparison.Ordinal);
        Assert.Contains(".book-settings-card.book-settings-s3 { grid-column:1/-1; width:100%; align-items:stretch; justify-content:stretch; }", layout, StringComparison.Ordinal);
        Assert.Contains("const renderProcessedInteriorPages", script, StringComparison.Ordinal);
        Assert.Contains("const introPageSize = 6", script, StringComparison.Ordinal);
        Assert.Contains("data-action=\"intro-template-page\"", script, StringComparison.Ordinal);
        Assert.Contains("const refreshIntroTemplateWorkspace", script, StringComparison.Ordinal);
        Assert.Contains("workspace.outerHTML = renderIntroTemplateWorkspace", script, StringComparison.Ordinal);
        Assert.DoesNotContain("tabButton(\"validation\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("tabButton(\"processing\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("tabButton(\"outputs\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("tabButton(\"logs\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionUsesBorderLegendGroupsWithPairedPreviewsAndTopFinalInterior()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "js", "app.js"));
        var layout = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "css", "book-workspace.css"));

        Assert.Contains("<legend>${escapeHtml(label)}</legend>", script, StringComparison.Ordinal);
        Assert.Contains("<legend>Final Interior</legend>", script, StringComparison.Ordinal);
        Assert.Contains("Source image", script, StringComparison.Ordinal);
        Assert.Contains("Preview image", script, StringComparison.Ordinal);
        Assert.Contains("const coverPreviewUrl = valueFor(coverOutput, \"thumbnailImageUrl\", \"\")", script, StringComparison.Ordinal);
        Assert.DoesNotContain("<h3>Production Assets</h3>", script, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"production-context\"", script, StringComparison.Ordinal);
        Assert.Contains(".production-group-grid { display:grid; grid-template-columns:minmax(0,1fr);", layout, StringComparison.Ordinal);
        Assert.Contains(".production-asset-layout { display:grid; grid-template-columns:minmax(0,5fr) minmax(0,5fr) minmax(0,2fr);", layout, StringComparison.Ordinal);
        Assert.Contains("class=\"production-asset-controls\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain(".production-preview-grid", layout, StringComparison.Ordinal);
        Assert.Contains(".production-group>legend", layout, StringComparison.Ordinal);
        Assert.Contains(".production-final-action { grid-column:1/-1;", layout, StringComparison.Ordinal);
        Assert.Contains("<legend>Suggested PDF filenames</legend>", script, StringComparison.Ordinal);
        Assert.Contains("Cover PDF filename", script, StringComparison.Ordinal);
        Assert.Contains("Interior PDF filename", script, StringComparison.Ordinal);
        Assert.Contains("readonly aria-describedby=\"production-pdf-name-help production-pdf-name-error\"", script, StringComparison.Ordinal);
        Assert.Contains(".production-pdf-name-grid { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)) auto;", layout, StringComparison.Ordinal);
        Assert.Contains(".production-pdf-name-error[hidden] { display:none; }", layout, StringComparison.Ordinal);
        Assert.Contains("@media (max-width:900px)", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void BookDetailKeepsInteriorActionsAndRemovesTheLegacyOverviewSummary()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "js", "app.js"));

        Assert.Contains("Run Interior preflight", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Review the summary and Brand background before processing", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Pages (interior)", script, StringComparison.Ordinal);
        Assert.Contains("renderFrameModeMigrationWarning(summary)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Full-book preflight", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BookCatalogUiExposesMetadataAssignmentAndExactBrandFiltering()
    {
        var frontend = Path.Combine(AppContext.BaseDirectory, "Frontend");
        var script = File.ReadAllText(Path.Combine(frontend, "js", "app.js"));
        var markup = File.ReadAllText(Path.Combine(frontend, "index.html"));

        Assert.Contains("Book Information", script, StringComparison.Ordinal);
        Assert.Contains("field(\"Subcover\", \"subcover\")", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Short description, usually 4–5 words; fewer than 100 characters", script, StringComparison.Ordinal);
        Assert.DoesNotContain("maxlength=\"99\"", script, StringComparison.Ordinal);
        Assert.Contains("bookMetadataValidation", script, StringComparison.Ordinal);
        Assert.Contains("validateBookMetadataDraft", script, StringComparison.Ordinal);
        Assert.Contains("data-action=\"save-book-metadata\"", script, StringComparison.Ordinal);
        Assert.Contains("data-action=\"assign-book-brand\"", script, StringComparison.Ordinal);
        Assert.Contains("data-action=\"unassign-book-brand\"", script, StringComparison.Ordinal);
        Assert.Contains("data-action=\"book-brand-filter\"", script, StringComparison.Ordinal);
        Assert.Contains("assignedBrandName(summary) === state.bookBrandFilter", script, StringComparison.Ordinal);
        Assert.Contains("state.selectedBookIds.clear()", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Processing Brand", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("brand-select", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("selectedBrand", script, StringComparison.Ordinal);
        Assert.DoesNotContain("activeBrand(", script, StringComparison.Ordinal);
        Assert.Contains("assignedBrandFor", script, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogUiWarnsBeforeInvalidatingAssignmentsAndBlocksUnsafeProcessing()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "js", "app.js"));

        Assert.Contains("metadataAssignmentWarning", script, StringComparison.Ordinal);
        Assert.Contains("assignmentReadiness", script, StringComparison.Ordinal);
        Assert.Contains("Reassign this Book from", script, StringComparison.Ordinal);
        Assert.Contains("Existing files and outputs will not be moved or changed", script, StringComparison.Ordinal);
        Assert.Contains("The existing assignment is preserved", script, StringComparison.Ordinal);
        Assert.Contains("Only Brands whose Author matches", script, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"${errorId}\"", script, StringComparison.Ordinal);
        Assert.Contains("patchBookMetadataValidationUi", script, StringComparison.Ordinal);
        Assert.Contains("scrollIntoView?.({ block: \"nearest\" })", script, StringComparison.Ordinal);
        Assert.DoesNotContain("subcoverTouchedBooks", script, StringComparison.Ordinal);
        Assert.Contains("const refreshBookCatalogCards", script, StringComparison.Ordinal);
        Assert.Contains("const preserveCatalogDrawer", script, StringComparison.Ordinal);
        Assert.Contains("refreshBookListRow(state.selectedBookId)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("card.replaceWith", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BrandCatalogUiSupportsOneAuthorAndSurfacesImpactBeforeSave()
    {
        var frontend = Path.Combine(AppContext.BaseDirectory, "Frontend");
        var script = File.ReadAllText(Path.Combine(frontend, "js", "app.js"));
        var layout = File.ReadAllText(Path.Combine(frontend, "css", "book-workspace.css"));

        Assert.Contains("Brand Information", script, StringComparison.Ordinal);
        Assert.Contains("One Brand has one Primary Author", script, StringComparison.Ordinal);
        Assert.Contains("data-action=\"brand-author-input\"", script, StringComparison.Ordinal);
        Assert.Contains("data-action=\"save-brand-author\"", script, StringComparison.Ordinal);
        Assert.Contains("assigned Book${impactedBooks === 1", script, StringComparison.Ordinal);
        Assert.Contains("metadataStatus === \"Unavailable\"", script, StringComparison.Ordinal);
        Assert.Contains("class=\"brand-row ${selected ? \"brand-row-active\" : \"\"}\"", script, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=\"${selected}\"", script, StringComparison.Ordinal);
        Assert.Contains("data-brand-result-count aria-live=\"polite\"", script, StringComparison.Ordinal);
        Assert.Contains(".brands-page { display:grid; grid-template-rows:auto minmax(0,1fr);", layout, StringComparison.Ordinal);
        Assert.Contains(".brand-workspace { display:grid; grid-template-columns:minmax(280px,3fr) minmax(0,9fr);", layout, StringComparison.Ordinal);
        Assert.Contains(".brand-list-scroll { min-height:0;", layout, StringComparison.Ordinal);
        Assert.Contains(".brand-detail-scroll { min-height:0;", layout, StringComparison.Ordinal);
        Assert.Contains("@media (max-width:900px)", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationUsesOneScrollablePanelWithPurposeBuiltGroups()
    {
        var frontend = Path.Combine(AppContext.BaseDirectory, "Frontend");
        var script = File.ReadAllText(Path.Combine(frontend, "js", "app.js"));
        var layout = File.ReadAllText(Path.Combine(frontend, "css", "book-workspace.css"));

        Assert.Contains("class=\"configuration-page\"", script, StringComparison.Ordinal);
        Assert.Contains("class=\"panel configuration-panel\" data-form=\"configuration\"", script, StringComparison.Ordinal);
        Assert.Contains("Processing capacity", script, StringComparison.Ordinal);
        Assert.Contains("Keyword Builder defaults", script, StringComparison.Ordinal);
        Assert.Contains("Artwork preparation", script, StringComparison.Ordinal);
        Assert.Contains("Working canvas", script, StringComparison.Ordinal);
        Assert.Contains("Final Interior output", script, StringComparison.Ordinal);
        Assert.Contains("Border search range", script, StringComparison.Ordinal);
        Assert.Contains("Border tolerances", script, StringComparison.Ordinal);
        Assert.Contains("Border acceptance rules", script, StringComparison.Ordinal);
        Assert.Contains("content.addEventListener(\"submit\"", script, StringComparison.Ordinal);
        Assert.Contains("state.settingsSavePending", script, StringComparison.Ordinal);
        Assert.Contains(".configuration-page { display:grid; grid-template-rows:auto minmax(0,1fr);", layout, StringComparison.Ordinal);
        Assert.Contains(".configuration-panel-scroll { min-height:0;", layout, StringComparison.Ordinal);
        Assert.Contains(".configuration-group-grid { display:grid; grid-template-columns:repeat(2,minmax(0,1fr));", layout, StringComparison.Ordinal);
        Assert.Contains("@media (max-width:900px)", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessInteriorUsesOneUnifiedSixSixTwelveWorkspace()
    {
        var frontend = Path.Combine(AppContext.BaseDirectory, "Frontend");
        var script = File.ReadAllText(Path.Combine(frontend, "js", "app.js"));
        var layout = File.ReadAllText(Path.Combine(frontend, "css", "book-workspace.css"));

        Assert.Contains("class=\"process-workspace\"", script, StringComparison.Ordinal);
        Assert.Contains("${summaryPanel}${currentStagePanel}${queuePanel}", script, StringComparison.Ordinal);
        Assert.DoesNotContain("processTab", script, StringComparison.Ordinal);
        Assert.DoesNotContain("data-action=\"process-tab\"", script, StringComparison.Ordinal);
        Assert.Contains("processQueuePageSize = 12", script, StringComparison.Ordinal);
        Assert.Contains("preserveProcessWorkspaceUi", script, StringComparison.Ordinal);
        Assert.Contains("restoreProcessWorkspaceUi", script, StringComparison.Ordinal);
        Assert.Contains("role=\"status\" aria-live=\"polite\" aria-atomic=\"true\"", script, StringComparison.Ordinal);
        Assert.Contains(".process-workspace { display:grid; grid-template-columns:repeat(12,minmax(0,1fr));", layout, StringComparison.Ordinal);
        Assert.Contains(".process-summary-panel,.process-current-stage-panel { grid-column:span 6;", layout, StringComparison.Ordinal);
        Assert.Contains(".process-queue-workspace { display:grid; grid-column:1/-1;", layout, StringComparison.Ordinal);
        Assert.Contains(".process-queue-grid-scroll { min-height:0; overflow-y:auto;", layout, StringComparison.Ordinal);
        Assert.Contains("@media (max-width:1080px)", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void KeywordBuilderUsesFixedKeywordInputsAndTabSeparatedClipboardContract()
    {
        var frontend = Path.Combine(AppContext.BaseDirectory, "Frontend");
        var script = File.ReadAllText(Path.Combine(frontend, "js", "app.js"));
        var layout = File.ReadAllText(Path.Combine(frontend, "css", "book-workspace.css"));

        Assert.Contains("Generic Keywords", script, StringComparison.Ordinal);
        Assert.Contains("Book Keywords", script, StringComparison.Ordinal);
        Assert.Contains("rows=\"5\" data-generic-keywords", script, StringComparison.Ordinal);
        Assert.Contains("rows=\"5\" data-action=\"book-keyword-source\"", script, StringComparison.Ordinal);
        Assert.Contains("type=\"text\" data-action=\"book-keyword-ads-asin\"", script, StringComparison.Ordinal);
        Assert.Contains("Copy to Clipboard", script, StringComparison.Ordinal);
        Assert.Contains("fields.join(\"\\t\")", script, StringComparison.Ordinal);
        Assert.Contains(".keyword-list-input.control { height:7.625rem; min-height:7.625rem; max-height:7.625rem;", layout, StringComparison.Ordinal);
        Assert.Contains("resize:none; overflow-y:auto;", layout, StringComparison.Ordinal);
        Assert.Contains(".keyword-builder-grid { display:grid; grid-template-columns:repeat(2,minmax(0,1fr));", layout, StringComparison.Ordinal);
        Assert.Contains(".keyword-builder-column { display:grid; grid-template-rows:auto minmax(0,1fr);", layout, StringComparison.Ordinal);
        Assert.Contains(".keyword-builder-group { display:grid;", layout, StringComparison.Ordinal);
        Assert.Contains(".keyword-builder-group>legend", layout, StringComparison.Ordinal);
        Assert.Contains("field keyword-builder-source-field", script, StringComparison.Ordinal);
        Assert.Contains(".keyword-builder-source-field .keyword-list-input.control { height:7.625rem;", layout, StringComparison.Ordinal);
        Assert.Contains(".keyword-builder-actions { display:grid; grid-template-columns:repeat(3,minmax(0,1fr));", layout, StringComparison.Ordinal);
        Assert.Contains("keyword-builder-group keyword-builder-inputs", script, StringComparison.Ordinal);
        Assert.Contains("keyword-builder-group keyword-builder-crawl", script, StringComparison.Ordinal);
        Assert.Contains("keyword-builder-group keyword-builder-outputs", script, StringComparison.Ordinal);
        Assert.DoesNotContain("catalog-card keyword-builder-card", script, StringComparison.Ordinal);
        Assert.Contains("<legend id=\"keyword-builder-inputs-title\">Build inputs</legend>", script, StringComparison.Ordinal);
        Assert.Contains("<legend id=\"asin-research-results-title\">Crawl Results</legend>", script, StringComparison.Ordinal);
        Assert.Contains("<legend id=\"keyword-builder-output-title\">Generated output</legend>", script, StringComparison.Ordinal);
        var generatedOutputStart = script.IndexOf("<fieldset class=\"keyword-builder-group keyword-builder-outputs\"", StringComparison.Ordinal);
        var adsAsinStart = script.IndexOf("<label class=\"field\" for=\"book-keyword-ads-asin\">", StringComparison.Ordinal);
        var actionsStart = script.IndexOf("<div class=\"keyword-builder-actions\">", StringComparison.Ordinal);
        var shuffleButtonStart = script.IndexOf("data-action=\"shuffle-book-keywords\"", StringComparison.Ordinal);
        var saveButtonStart = script.IndexOf("data-action=\"save-book-keywords\"", StringComparison.Ordinal);
        var copyButtonStart = script.IndexOf("data-action=\"copy-book-keywords\"", StringComparison.Ordinal);
        Assert.True(generatedOutputStart >= 0 && adsAsinStart > generatedOutputStart, "Ads ASIN must be inside Generated output.");
        Assert.True(actionsStart > adsAsinStart && shuffleButtonStart > actionsStart && saveButtonStart > shuffleButtonStart && copyButtonStart > saveButtonStart, "Shuffle, Save, and Copy must share the final Generated output action row.");
        Assert.DoesNotContain("remaining word", script, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"keyword-builder-inputs-title\"", script, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"keyword-builder-output-title\"", script, StringComparison.Ordinal);
        Assert.Contains("const renderAsinResearchWorkspace", script, StringComparison.Ordinal);
        Assert.Contains("${renderBookKeywordBuilder(book, summary)}", script, StringComparison.Ordinal);
        Assert.Contains("${renderAsinResearch(book, summary)}", script, StringComparison.Ordinal);
        Assert.Contains("? renderAsinResearchWorkspace(book, summary)", script, StringComparison.Ordinal);
        Assert.Contains("state.selectedBookTab !== \"asin\"", script, StringComparison.Ordinal);
        Assert.Contains(".asin-research-workspace { display:grid; gap:20px; min-width:0; }", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void KeywordBuilderAsinResearchHasScopedResponsiveAndAccessibleUi()
    {
        var frontend = Path.Combine(AppContext.BaseDirectory, "Frontend");
        var script = File.ReadAllText(Path.Combine(frontend, "js", "app.js"));
        var layout = File.ReadAllText(Path.Combine(frontend, "css", "book-workspace.css"));

        Assert.DoesNotContain("data-action=\"asin-search-keywords\"", script, StringComparison.Ordinal);
        Assert.Contains("Uses generated Ads Keyword", script, StringComparison.Ordinal);
        Assert.Contains("tabButton(\"asin\", \"Keyword\")", script, StringComparison.Ordinal);
        Assert.DoesNotContain("tabButton(\"asin\", \"ASIN Research\")", script, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"asin-research-results-title\"", script, StringComparison.Ordinal);
        Assert.Contains("role=\"progressbar\"", script, StringComparison.Ordinal);
        Assert.Contains("const patchAsinResearch", script, StringComparison.Ordinal);
        Assert.Contains("const autoStageAsinCrawlResults", script, StringComparison.Ordinal);
        Assert.Contains("researchDraft.autoApplyPending = true", script, StringComparison.Ordinal);
        Assert.DoesNotContain("!shouldApply || outcome !== \"Completed\"", script, StringComparison.Ordinal);
        Assert.Contains("Crawled ASINs added to the preview. Save when ready.", script, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"asin-research-result\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("data-action=\"copy-amazon-asins\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("data-action=\"use-amazon-asins\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("!validInput || active || browserBusy || needsAttention", script, StringComparison.Ordinal);
        Assert.DoesNotContain("section.outerHTML = renderAsinResearch", script, StringComparison.Ordinal);
        Assert.Contains("syncContent(\".asin-result-list\")", script, StringComparison.Ordinal);
        Assert.Contains(".keyword-builder-crawl { grid-template-rows:auto auto auto minmax(10.5rem,1fr) auto auto auto;", layout, StringComparison.Ordinal);
        Assert.Contains(".keyword-builder-crawl .asin-result-list { height:10.5rem; min-height:10.5rem; max-height:10.5rem; }", layout, StringComparison.Ordinal);
        Assert.Contains("@media (max-width:760px)", layout, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion:reduce", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundedBookWorkspaceKeepsPreviewLoadingAndMotionAccessibilityContracts()
    {
        var frontend = Path.Combine(AppContext.BaseDirectory, "Frontend");
        var script = File.ReadAllText(Path.Combine(frontend, "js", "app.js"));
        var baseStyles = File.ReadAllText(Path.Combine(frontend, "css", "tailwind.css"));
        var workspaceStyles = File.ReadAllText(Path.Combine(frontend, "css", "book-workspace.css"));

        Assert.Contains("localImageMarkup", script, StringComparison.Ordinal);
        Assert.Contains("book-detail-title", script, StringComparison.Ordinal);
        Assert.Contains("Preview unavailable", script, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion", baseStyles, StringComparison.Ordinal);
        Assert.Contains("overflow-x: hidden", workspaceStyles, StringComparison.Ordinal);
        Assert.DoesNotContain("file://", script, StringComparison.OrdinalIgnoreCase);
    }
}
