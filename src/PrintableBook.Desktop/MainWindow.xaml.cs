using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using PrintableBook.Core.Application.Services;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Desktop.Bridge;
using System.Windows;

namespace PrintableBook.Desktop;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly WebViewBridgeRouter bridgeRouter;

    private readonly IInterruptedProcessingRecoveryService interruptedRecoveryService;

    public MainWindow(IPrintableBookApplication application, IApplicationSnapshotService snapshotService, IGlobalSettingsStore settingsStore, IProcessSessionService processSessionService, IApplicationRootDiscovery rootDiscovery, IBrandSettingsStore brandSettingsStore, IBookCoverSelectionService coverSelectionService, IInteriorFrameModeService interiorFrameModeService, IInterruptedProcessingRecoveryService interruptedRecoveryService)
    {
        Application = application;
        this.interruptedRecoveryService = interruptedRecoveryService;
        bridgeRouter = new WebViewBridgeRouter(snapshotService, settingsStore, processSessionService, rootDiscovery, brandSettingsStore, coverSelectionService, interiorFrameModeService);
        InitializeComponent();
    }

    internal IPrintableBookApplication Application { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await interruptedRecoveryService.RecoverAsync();
            await Browser.EnsureCoreWebView2Async();
            Browser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            var pagePath = Path.Combine(AppContext.BaseDirectory, "Frontend", "index.html");
            Browser.CoreWebView2.Navigate(new Uri(pagePath).AbsoluteUri);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"The application could not recover interrupted processing.\n\n{exception.Message}",
                "Startup recovery failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        BridgeResponse response;
        try
        {
            response = await bridgeRouter.HandleAsync(WebViewMessageReader.ReadOrNull(e.TryGetWebMessageAsString));
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            response = BridgeResponse.Failed("desktop-message", "desktop_bridge_failed", exception);
        }

        Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(response, JsonOptions));
    }
}
