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
}
