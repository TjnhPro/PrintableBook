using System.Text.Json;

namespace PrintableBook.Desktop.Bridge;

/// <summary>
/// Parses and routes the narrow, versioned messages accepted from the WebView.
/// </summary>
internal sealed class WebViewBridgeRouter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public const int Version = 1;

    public BridgeResponse Handle(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return BridgeResponse.InvalidRequest();
        }

        try
        {
            var request = JsonSerializer.Deserialize<BridgeRequest>(json, JsonOptions);
            if (request is null ||
                request.Version != Version ||
                string.IsNullOrWhiteSpace(request.Id) ||
                string.IsNullOrWhiteSpace(request.Command))
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
}
