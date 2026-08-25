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
    public void BookLibraryContractIncludesPaginatedGridFiltersAndTextualStatusFeedback()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "js", "app.js"));

        Assert.Contains("const pageSize = 12", script, StringComparison.Ordinal);
        Assert.Contains("Needs review", script, StringComparison.Ordinal);
        Assert.Contains("PDF ready", script, StringComparison.Ordinal);
        Assert.Contains("book-grid", script, StringComparison.Ordinal);
        Assert.Contains("localImageMarkup(cover, `Cover for ${valueFor(item, \"name\", \"\")}`)", script, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", script, StringComparison.Ordinal);
        Assert.Contains(".book-secondary-filters { align-items:flex-end", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "css", "book-workspace.css")), StringComparison.Ordinal);
    }

    [Fact]
    public void BookDetailUsesAnAccessibleDismissibleDrawer()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "js", "app.js"));
        var layout = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "css", "book-workspace.css"));

        Assert.Contains("book-drawer", script, StringComparison.Ordinal);
        Assert.Contains("role=\"dialog\"", script, StringComparison.Ordinal);
        Assert.Contains("close-book-drawer", script, StringComparison.Ordinal);
        Assert.Contains("event.key !== \"Escape\"", script, StringComparison.Ordinal);
        Assert.Contains("width:75vw", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("width:min(720px,52vw)", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void AssetWorkspaceGroupsKnownFoldersAndQueuesOnlyVisiblePreviews()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "js", "app.js"));
        var layout = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "css", "book-workspace.css"));

        Assert.Contains("queueVisibleAssetPreviews", script, StringComparison.Ordinal);
        Assert.Contains("state.activePreviewRequests < 4", script, StringComparison.Ordinal);
        Assert.Contains("asset-folder-group", script, StringComparison.Ordinal);
        Assert.Contains("Every discovered Interior page is included in this run.", script, StringComparison.Ordinal);
        Assert.Contains("assetSearchFocused", script, StringComparison.Ordinal);
        Assert.Contains("search.setSelectionRange", script, StringComparison.Ordinal);
        Assert.Contains("updateVisibleAssetPreview", script, StringComparison.Ordinal);
        Assert.Contains("if (isViewingInteriorAssets) updateVisibleAssetPreview(preview);", script, StringComparison.Ordinal);
        Assert.Contains("--pb-asset-preview: 1 / 1", layout, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns:repeat(3,minmax(0,1fr))", layout, StringComparison.Ordinal);
        Assert.Contains("@media (min-width:1280px)", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void InteriorAssetsExposePerAssetFrameModeControlsWithoutABulkMutation()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "js", "app.js"));

        Assert.Contains("Every discovered Interior page is included in this run.", script, StringComparison.Ordinal);
        Assert.Contains("<span>Frame mode</span>", script, StringComparison.Ordinal);
        Assert.Contains("data-action=\"set-interior-frame-mode\"", script, StringComparison.Ordinal);
        Assert.Contains("data-source-reference", script, StringComparison.Ordinal);
        Assert.DoesNotContain("book.interior.frame-mode.batch", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BookDetailIsInteriorOnlyAndKeepsTheOverviewToASummary()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "js", "app.js"));

        Assert.Contains("Interior-only preflight checks the source pages", script, StringComparison.Ordinal);
        Assert.Contains("!valueFor(check, \"code\", \"\").startsWith(\"book.cover_\")", script, StringComparison.Ordinal);
        Assert.Contains("Use Interior assets to review page previews", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Full-book preflight", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundedBookWorkspaceKeepsPreviewLoadingAndMotionAccessibilityContracts()
    {
        var frontend = Path.Combine(AppContext.BaseDirectory, "Frontend");
        var script = File.ReadAllText(Path.Combine(frontend, "js", "app.js"));
        var baseStyles = File.ReadAllText(Path.Combine(frontend, "css", "tailwind.css"));
        var workspaceStyles = File.ReadAllText(Path.Combine(frontend, "css", "book-workspace.css"));

        Assert.Contains("queueVisibleAssetPreviews", script, StringComparison.Ordinal);
        Assert.Contains("book-drawer-title", script, StringComparison.Ordinal);
        Assert.Contains("Preview unavailable", script, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion", baseStyles, StringComparison.Ordinal);
        Assert.Contains("overflow-x: hidden", workspaceStyles, StringComparison.Ordinal);
        Assert.DoesNotContain("file://", script, StringComparison.OrdinalIgnoreCase);
    }
}
