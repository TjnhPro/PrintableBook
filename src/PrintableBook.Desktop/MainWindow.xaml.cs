using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using PrintableBook.Core.Application.Services;
using System.Windows;

namespace PrintableBook.Desktop;

public partial class MainWindow : Window
{
    private const int BridgeVersion = 1;

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
        var response = TryHandleMessage(e.TryGetWebMessageAsString());
        Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(response));
    }

    private static BridgeResponse TryHandleMessage(string json)
    {
        try
        {
            var request = JsonSerializer.Deserialize<BridgeRequest>(json);
            if (request is null || request.Version != BridgeVersion || string.IsNullOrWhiteSpace(request.Id))
            {
                return BridgeResponse.InvalidRequest();
            }

            return request.Command switch
            {
                "app.ping" => BridgeResponse.Pong(request.Id),
                _ => BridgeResponse.UnsupportedCommand(request.Id)
            };
        }
        catch (JsonException)
        {
            return BridgeResponse.InvalidRequest();
        }
    }

    private sealed record BridgeRequest(int Version, string Id, string Command);

    private sealed record BridgeResponse(int Version, string? Id, bool Ok, string? Command, string? Error)
    {
        public static BridgeResponse Pong(string id) => new(BridgeVersion, id, true, "app.pong", null);

        public static BridgeResponse InvalidRequest() => new(BridgeVersion, null, false, null, "invalid_request");

        public static BridgeResponse UnsupportedCommand(string id) => new(BridgeVersion, id, false, null, "unsupported_command");
    }
}
