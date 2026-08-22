using System.IO;
using Microsoft.Web.WebView2.Core;
using PrintableBook.Core.Application.Services;
using PrintableBook.Desktop.Bridge;
using System.Windows;

namespace PrintableBook.Desktop;

public partial class MainWindow : Window
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);
    private readonly WebViewBridgeRouter bridgeRouter = new();

    public MainWindow(IPrintableBookApplication application)
    {
        Application = application;
        InitializeComponent();
    }

    internal IPrintableBookApplication Application { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await Browser.EnsureCoreWebView2Async();
        Browser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        var pagePath = Path.Combine(AppContext.BaseDirectory, "Frontend", "index.html");
        Browser.CoreWebView2.Navigate(new Uri(pagePath).AbsoluteUri);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var response = bridgeRouter.Handle(e.TryGetWebMessageAsString());
        Browser.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(response, JsonOptions));
    }
}
