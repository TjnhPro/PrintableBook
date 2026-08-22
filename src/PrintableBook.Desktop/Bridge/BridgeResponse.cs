namespace PrintableBook.Desktop.Bridge;

internal sealed record BridgeResponse(int Version, string? Id, bool Ok, string? Command, string? Error, object? Payload = null)
{
    public static BridgeResponse Pong(string id) => new(WebViewBridgeRouter.Version, id, true, "app.pong", null);

    public static BridgeResponse InvalidRequest() => new(WebViewBridgeRouter.Version, null, false, null, "invalid_request");

    public static BridgeResponse UnsupportedCommand(string id) =>
        new(WebViewBridgeRouter.Version, id, false, null, "unsupported_command");

    public static BridgeResponse Succeeded(string id, string command, object payload) =>
        new(WebViewBridgeRouter.Version, id, true, command, null, payload);

    public static BridgeResponse Failed(string id, string code, Exception exception) =>
        new(WebViewBridgeRouter.Version, id, false, null, $"{code}: {exception.Message}");
}
