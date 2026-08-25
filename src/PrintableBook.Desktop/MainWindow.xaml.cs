using System.IO;
using System.Text.Json;
using System.ComponentModel;
using Microsoft.Web.WebView2.Core;
using PrintableBook.Core.Application.Services;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Desktop.Bridge;
using PrintableBook.Desktop.Loading;
using PrintableBook.Desktop.Diagnostics;
using PrintableBook.Desktop.Preview;
using PrintableBook.Core.Application.Diagnostics;
using PrintableBook.Core.Application.BackgroundTasks;
using System.Windows;

namespace PrintableBook.Desktop;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly WebViewBridgeRouter bridgeRouter;
    private readonly ProcessWindowShutdownCoordinator shutdownCoordinator;
    private readonly DispatcherStallMonitor dispatcherStallMonitor;
    private readonly CancellationTokenSource closeFlowCancellation = new();
    private bool allowClose;
    private bool closeFlowRunning;
    private bool systemShutdown;

    public MainWindow(IPrintableBookApplication application, ApplicationLoadCoordinator applicationLoadCoordinator, IGlobalSettingsStore settingsStore, IProcessSessionService processSessionService, IApplicationRootDiscovery rootDiscovery, IBrandSettingsStore brandSettingsStore, IBookCoverSelectionService coverSelectionService, IInteriorFrameModeService interiorFrameModeService, BookAssetPreviewCoordinator bookAssetPreviewCoordinator, ILocalOutputActionService outputActionService, IOperationDiagnostics diagnostics, UiDiagnosticsService uiDiagnosticsService, IBackgroundTaskManager backgroundTaskManager, DispatcherStallMonitor dispatcherStallMonitor, ProcessWindowShutdownCoordinator shutdownCoordinator)
    {
        Application = application;
        this.shutdownCoordinator = shutdownCoordinator;
        this.dispatcherStallMonitor = dispatcherStallMonitor;
        bridgeRouter = new WebViewBridgeRouter(applicationLoadCoordinator, settingsStore, processSessionService, rootDiscovery, brandSettingsStore, coverSelectionService, interiorFrameModeService, bookAssetPreviewCoordinator, outputActionService, diagnostics, uiDiagnosticsService, backgroundTaskManager);
        InitializeComponent();
        dispatcherStallMonitor.Start();
        Closing += OnClosing;
        Closed += OnClosed;
    }

    internal IPrintableBookApplication Application { get; }

    internal void BeginSystemShutdown()
    {
        systemShutdown = true;

        try
        {
            closeFlowCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Window lifecycle already completed.
        }
    }

    internal static bool ShouldHandleInteractiveClose(bool allowClose, bool systemShutdown) =>
        !allowClose && !systemShutdown;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await Browser.EnsureCoreWebView2Async();
            Browser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            var pagePath = Path.Combine(AppContext.BaseDirectory, "Frontend", "index.html");
            Browser.CoreWebView2.Navigate(new Uri(pagePath).AbsoluteUri);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"The application UI could not start.\n\n{exception.Message}",
                "Startup failed",
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

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!ShouldHandleInteractiveClose(allowClose, systemShutdown)) return;
        e.Cancel = true;
        if (closeFlowRunning) return;
        closeFlowRunning = true;

        try
        {
            // Let the Closing event return after it has been cancelled before a completed
            // coordinator result can attempt to invoke Close again.
            await Task.Yield();
            switch (await shutdownCoordinator.RequestCloseAsync(closeFlowCancellation.Token))
            {
                case ProcessWindowCloseOutcome.KeepOpen:
                    closeFlowRunning = false;
                    return;
                case ProcessWindowCloseOutcome.Close:
                    allowClose = true;
                    Close();
                    return;
                case ProcessWindowCloseOutcome.ForceExit:
                    Environment.Exit(0);
                    return;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        catch (OperationCanceledException)
        {
            closeFlowRunning = false;
        }
        catch (Exception exception)
        {
            closeFlowRunning = false;
            MessageBox.Show(
                $"The application could not stop processing cleanly.\n\n{exception.Message}",
                "Shutdown failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        dispatcherStallMonitor.Dispose();
        closeFlowCancellation.Dispose();
    }
}
