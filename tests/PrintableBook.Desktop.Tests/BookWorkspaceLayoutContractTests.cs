namespace PrintableBook.Desktop.Tests;

public sealed class BookWorkspaceLayoutContractTests
{
    [Fact]
    public void WebViewShellUsesABoundedCanvasInsteadOfAForcedMinimumPageWidth()
    {
        var frontend = Path.Combine(AppContext.BaseDirectory, "Frontend");
        var markup = File.ReadAllText(Path.Combine(frontend, "index.html"));
        var layout = File.ReadAllText(Path.Combine(frontend, "css", "book-workspace.css"));

        Assert.DoesNotContain("min-w-[1600px]", markup, StringComparison.Ordinal);
        Assert.Contains("pb-app-canvas", markup, StringComparison.Ordinal);
        Assert.Contains("--pb-webview-width: 1600px", layout, StringComparison.Ordinal);
        Assert.Contains("--pb-webview-height: 900px", layout, StringComparison.Ordinal);
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
        Assert.Contains("role=\"status\"", script, StringComparison.Ordinal);
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
        Assert.Contains("width:min(720px,52vw)", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void AssetWorkspaceGroupsKnownFoldersAndQueuesOnlyVisiblePreviews()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "js", "app.js"));
        var layout = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "css", "book-workspace.css"));

        Assert.Contains("queueVisibleAssetPreviews", script, StringComparison.Ordinal);
        Assert.Contains("state.activePreviewRequests < 4", script, StringComparison.Ordinal);
        Assert.Contains("asset-folder-group", script, StringComparison.Ordinal);
        Assert.Contains("Folder is missing from this Book.", script, StringComparison.Ordinal);
        Assert.Contains("--pb-asset-preview: 1 / 1", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void InteriorAssetsExposePerAssetFrameModeControlsWithoutABulkMutation()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Frontend", "js", "app.js"));

        Assert.Contains("Auto · Not classified", script, StringComparison.Ordinal);
        Assert.Contains("data-action=\"set-interior-frame-mode\"", script, StringComparison.Ordinal);
        Assert.Contains("data-source-reference", script, StringComparison.Ordinal);
        Assert.DoesNotContain("book.interior.frame-mode.batch", script, StringComparison.Ordinal);
    }
}
