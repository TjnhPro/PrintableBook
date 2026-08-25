namespace PrintableBook.Desktop.Tests;

public sealed class DesktopStartupContractTests
{
    [Fact]
    public void MainWindow_navigates_the_webview_without_waiting_for_interrupted_processing_recovery()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src", "PrintableBook.Desktop", "MainWindow.xaml.cs"));

        Assert.DoesNotContain("await interruptedRecoveryService.RecoverAsync", source, StringComparison.Ordinal);
        Assert.Contains("await Browser.EnsureCoreWebView2Async", source, StringComparison.Ordinal);
        Assert.Contains("Browser.CoreWebView2.Navigate", source, StringComparison.Ordinal);
    }
}
