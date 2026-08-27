using System.Windows;

namespace PrintableBook.Desktop.Tests;

public sealed class DesktopStartupContractTests
{
    [Fact]
    public void MainWindow_clamps_the_preferred_size_to_the_current_work_area()
    {
        var fittedSize = MainWindow.ConstrainToWorkingArea(new Size(1650, 950), new Size(1536, 864));

        Assert.Equal(new Size(1536, 864), fittedSize);
    }

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
